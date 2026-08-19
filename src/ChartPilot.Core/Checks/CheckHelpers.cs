using System.Globalization;
using System.Text.RegularExpressions;
using ChartPilot.Core.Manifests;
using YamlDotNet.RepresentationModel;

namespace ChartPilot.Core.Checks;

/// <summary>A parsed container image reference.</summary>
internal sealed record ImageReference(string Raw, string Repository, string? Tag, string? Digest)
{
    public bool HasDigest => !string.IsNullOrEmpty(Digest);

    public bool IsLatest => Tag is null || string.Equals(Tag, "latest", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Shared, side-effect-free helpers used by the rule catalog. Everything here is a pure function of
/// the rendered YAML; nothing touches disk, a process or the clock.
/// </summary>
internal static class CheckHelpers
{
    /// <summary>Kinds that carry a pod template (or, for Pod, are one).</summary>
    internal static readonly string[] PodCarryingKinds =
        ["Deployment", "StatefulSet", "DaemonSet", "ReplicaSet", "Job", "CronJob", "Pod"];

    /// <summary>Workloads that stay up and are therefore expected to have probes, PDBs and NetworkPolicies.</summary>
    internal static readonly string[] LongLivedKinds = ["Deployment", "StatefulSet", "DaemonSet", "ReplicaSet"];

    /// <summary>The recommended app.kubernetes.io label set.</summary>
    internal static readonly string[] StandardLabels =
    [
        "app.kubernetes.io/name",
        "app.kubernetes.io/instance",
        "app.kubernetes.io/version",
        "app.kubernetes.io/managed-by",
        "app.kubernetes.io/part-of"
    ];

    /// <summary>Annotation keys any one of which counts as a declared owner / contact.</summary>
    internal static readonly string[] OwnershipAnnotations =
    [
        "chartpilot.io/owner",
        "chartpilot.io/contact",
        "app.kubernetes.io/owner",
        "owner",
        "team",
        "contact",
        "platform.io/owner",
        "platform/owner"
    ];

    /// <summary>Label keys any one of which counts as declared ownership.</summary>
    internal static readonly string[] OwnershipLabels =
    [
        "app.kubernetes.io/part-of",
        "owner",
        "team"
    ];

    private static readonly Regex SemverLikeTag = new(
        @"^v?\d+(\.\d+)*(-[0-9A-Za-z][0-9A-Za-z.-]*)?(\+[0-9A-Za-z][0-9A-Za-z.-]*)?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex SecretishName = new(
        @"password|passwd|secret|token|apikey|api_key|privatekey|private_key|credential|(^|[^A-Za-z])key([^A-Za-z]|$)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex GoDurationPart = new(
        @"(\d+(?:\.\d+)?)(ns|us|ms|s|m|h)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // ---------------------------------------------------------------- workloads

    internal static bool IsPodCarrying(RenderedResource resource)
        => Array.IndexOf(PodCarryingKinds, resource.Kind) >= 0;

    internal static bool IsLongLived(RenderedResource resource)
        => Array.IndexOf(LongLivedKinds, resource.Kind) >= 0;

    /// <summary>Every rendered resource that carries a pod template, ordered as the graph returns them.</summary>
    internal static IEnumerable<RenderedResource> Workloads(CheckContext context)
        => context.Graph.Workloads().Where(IsPodCarrying);

    internal static IEnumerable<RenderedResource> LongLivedWorkloads(CheckContext context)
        => context.Graph.Workloads().Where(IsLongLived);

    internal static bool HasWorkloads(CheckContext context) => Workloads(context).Any();

    internal static bool HasLongLivedWorkloads(CheckContext context) => LongLivedWorkloads(context).Any();

    /// <summary>Application containers, i.e. everything except init containers.</summary>
    internal static IEnumerable<ContainerNode> AppContainers(RenderedResource resource)
        => ManifestNavigator.GetContainers(resource).Where(c => !c.IsInitContainer);

    internal static IEnumerable<ContainerNode> InitContainers(RenderedResource resource)
        => ManifestNavigator.GetContainers(resource).Where(c => c.IsInitContainer);

    /// <summary>The pod labels of a workload: the pod template labels, or metadata labels for a bare Pod.</summary>
    internal static IReadOnlyDictionary<string, string> PodLabels(RenderedResource workload)
    {
        if (workload.Kind == "Pod")
        {
            return ManifestNavigator.GetLabels(workload);
        }

        var templatePath = workload.Kind == "CronJob"
            ? "spec.jobTemplate.spec.template.metadata.labels"
            : "spec.template.metadata.labels";

        var labels = ManifestNavigator.GetStringMap(workload.Root, templatePath);
        return labels.Count > 0 ? labels : ManifestNavigator.GetLabels(workload);
    }

    /// <summary>The dotted path of a field directly on a workload's pod spec.</summary>
    internal static string PodSpecField(RenderedResource resource, string field)
        => ManifestNavigator.PodSpecPath(resource) + "." + field;

    // ---------------------------------------------------------------- security context

    /// <summary>
    /// A boolean security-context field, resolved container-first and falling back to the pod
    /// security context — which is exactly how the kubelet resolves it.
    /// </summary>
    internal static bool? SecurityBool(RenderedResource resource, ContainerNode container, string field)
    {
        var fromContainer = ManifestNavigator.GetBool(container.Node, "securityContext." + field);
        if (fromContainer is not null)
        {
            return fromContainer;
        }

        return ManifestNavigator.GetBool(ManifestNavigator.GetPodSpec(resource), "securityContext." + field);
    }

    /// <summary>An integer security-context field (i.e. runAsUser), resolved container-first.</summary>
    internal static int? SecurityInt(RenderedResource resource, ContainerNode container, string field)
    {
        var fromContainer = ManifestNavigator.GetInt(container.Node, "securityContext." + field);
        if (fromContainer is not null)
        {
            return fromContainer;
        }

        return ManifestNavigator.GetInt(ManifestNavigator.GetPodSpec(resource), "securityContext." + field);
    }

    /// <summary>True when the container drops every Linux capability.</summary>
    internal static bool DropsAllCapabilities(ContainerNode container)
    {
        foreach (var node in ManifestNavigator.GetSequence(container.Node, "securityContext.capabilities.drop"))
        {
            if (node is YamlScalarNode { Value: { } value }
                && string.Equals(value, "ALL", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    // ---------------------------------------------------------------- images

    /// <summary>Splits registry/repo:tag or registry/repo@sha256:... into its parts.</summary>
    internal static ImageReference? ParseImage(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var image = raw.Trim();

        var at = image.IndexOf('@');
        if (at > 0)
        {
            var repositoryPart = image[..at];
            var digest = image[(at + 1)..];
            var colon = repositoryPart.LastIndexOf(':');
            var slash = repositoryPart.LastIndexOf('/');
            var tagged = colon > slash ? repositoryPart[(colon + 1)..] : null;
            var repo = colon > slash ? repositoryPart[..colon] : repositoryPart;
            return new ImageReference(image, repo, tagged, digest);
        }

        var lastColon = image.LastIndexOf(':');
        var lastSlash = image.LastIndexOf('/');

        return lastColon > lastSlash
            ? new ImageReference(image, image[..lastColon], image[(lastColon + 1)..], null)
            : new ImageReference(image, image, null, null);
    }

    /// <summary>True when the tag parses as a version rather than a moving label such as "stable".</summary>
    internal static bool IsVersionLikeTag(string? tag)
        => !string.IsNullOrWhiteSpace(tag) && SemverLikeTag.IsMatch(tag.Trim());

    // ---------------------------------------------------------------- secrets

    /// <summary>True when an environment variable name looks like it carries secret material.</summary>
    internal static bool LooksLikeSecretName(string? name)
        => !string.IsNullOrWhiteSpace(name) && SecretishName.IsMatch(name);

    // ---------------------------------------------------------------- graph edges

    // Relationship questions ("does a NetworkPolicy select this workload", "which Services does this
    // route reach") are answered from the pre-resolved edges the graph builder produced. The rules
    // never re-implement selector matching: there is exactly one place where a selector is read
    // (SelectorReader) and one place where a reference is resolved (ResourceGraphBuilder).

    /// <summary>The edges of one relation leaving a resource.</summary>
    internal static IEnumerable<GraphEdge> OutgoingEdges(CheckContext context, RenderedResource source, string relation)
        => context.Graph.EdgesFrom(source.Ref)
            .Where(e => string.Equals(e.Relation, relation, StringComparison.Ordinal));

    /// <summary>The rendered targets of one relation leaving a resource; unresolved targets are skipped.</summary>
    internal static IEnumerable<RenderedResource> Targets(CheckContext context, RenderedResource source, string relation)
    {
        foreach (var edge in OutgoingEdges(context, source, relation))
        {
            if (context.Graph.Resolve(edge.To) is { } target)
            {
                yield return target;
            }
        }
    }

    /// <summary>The rendered resources of a kind that point at <paramref name="target"/> through a relation.</summary>
    internal static IEnumerable<RenderedResource> Sources(
        CheckContext context,
        RenderedResource target,
        string relation,
        string kind)
    {
        foreach (var edge in context.Graph.EdgesTo(target.Ref))
        {
            if (!string.Equals(edge.Relation, relation, StringComparison.Ordinal)
                || !string.Equals(edge.From.Kind, kind, StringComparison.Ordinal))
            {
                continue;
            }

            if (context.Graph.Resolve(edge.From) is { } source)
            {
                yield return source;
            }
        }
    }

    /// <summary>True when at least one resource of the kind reaches the target through the relation.</summary>
    internal static bool IsReachedBy(CheckContext context, RenderedResource target, string relation, string kind)
        => Sources(context, target, relation, kind).Any();

    // ---------------------------------------------------------------- istio helpers

    internal static readonly string[] IstioKinds =
        ["VirtualService", "Gateway", "DestinationRule", "AuthorizationPolicy", "PeerAuthentication", "Sidecar"];

    internal static bool HasIstio(CheckContext context)
        => IstioKinds.Any(context.Graph.ContainsKind);

    /// <summary>
    /// Strips a namespace qualifier and a cluster-domain suffix from an Istio host or gateway
    /// reference: istio-system/public-gw and member-api.apps.svc.cluster.local both reduce to
    /// their short name.
    /// </summary>
    internal static string ShortName(string reference)
    {
        var value = reference.Trim();

        var slash = value.LastIndexOf('/');
        if (slash >= 0)
        {
            value = value[(slash + 1)..];
        }

        var dot = value.IndexOf('.');
        return dot > 0 ? value[..dot] : value;
    }

    /// <summary>The gateway references of a VirtualService, excluding the implicit mesh gateway.</summary>
    internal static IReadOnlyList<string> GatewayRefs(RenderedResource virtualService)
    {
        var result = new List<string>();

        foreach (var node in ManifestNavigator.GetSequence(virtualService.Root, "spec.gateways"))
        {
            if (node is YamlScalarNode { Value: { Length: > 0 } value }
                && !string.Equals(value, "mesh", StringComparison.Ordinal))
            {
                result.Add(value);
            }
        }

        return result;
    }

    /// <summary>Every route block of a VirtualService, with the dotted path that locates it.</summary>
    internal static IEnumerable<(YamlMappingNode Route, string Path, string Protocol)> Routes(
        RenderedResource virtualService)
    {
        foreach (var protocol in new[] { "http", "tls", "tcp" })
        {
            var routes = ManifestNavigator.GetSequence(virtualService.Root, "spec." + protocol);
            for (var i = 0; i < routes.Count; i++)
            {
                if (routes[i] is YamlMappingNode route)
                {
                    yield return (route, $"spec.{protocol}[{i}]", protocol);
                }
            }
        }
    }

    /// <summary>Every destination host a VirtualService routes to, with its dotted path.</summary>
    internal static IEnumerable<(string Host, string Path)> DestinationHosts(RenderedResource virtualService)
    {
        foreach (var (route, path, _) in Routes(virtualService))
        {
            var destinations = ManifestNavigator.GetSequence(route, "route");
            for (var i = 0; i < destinations.Count; i++)
            {
                var host = ManifestNavigator.GetString(destinations[i], "destination.host");
                if (!string.IsNullOrWhiteSpace(host))
                {
                    yield return (host, $"{path}.route[{i}].destination.host");
                }
            }
        }
    }

    /// <summary>
    /// The workloads a Service fronts. A Service with no selector (headless, ExternalName, manually
    /// managed endpoints) produced no <c>selects</c> edges and therefore fronts nothing.
    /// </summary>
    internal static IEnumerable<RenderedResource> WorkloadsBehind(CheckContext context, RenderedResource service)
        => Targets(context, service, GraphRelations.Selects).Where(IsPodCarrying);

    // ---------------------------------------------------------------- durations

    /// <summary>
    /// Parses a Go duration string of the form Kubernetes and cert-manager use (2160h, 720h30m,
    /// 1.5s). Returns null when the value is absent or unparseable.
    /// </summary>
    internal static TimeSpan? ParseGoDuration(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var text = value.Trim();
        var negative = text.StartsWith('-');
        if (negative || text.StartsWith('+'))
        {
            text = text[1..];
        }

        if (text.Length == 0)
        {
            return null;
        }

        var matches = GoDurationPart.Matches(text);
        if (matches.Count == 0)
        {
            return null;
        }

        // Reject anything with characters outside the matched unit groups, i.e. "2160hours".
        var consumed = matches.Sum(m => m.Length);
        if (consumed != text.Length)
        {
            return null;
        }

        var total = TimeSpan.Zero;

        foreach (Match match in matches)
        {
            if (!double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var amount))
            {
                return null;
            }

            total += match.Groups[2].Value switch
            {
                "ns" => TimeSpan.FromTicks((long)(amount / 100)),
                "us" => TimeSpan.FromTicks((long)(amount * 10)),
                "ms" => TimeSpan.FromMilliseconds(amount),
                "s" => TimeSpan.FromSeconds(amount),
                "m" => TimeSpan.FromMinutes(amount),
                "h" => TimeSpan.FromHours(amount),
                _ => TimeSpan.Zero
            };
        }

        return negative ? -total : total;
    }

    /// <summary>Renders a duration the way an operator would read it back in a manifest.</summary>
    internal static string DescribeDuration(TimeSpan duration)
        => duration.TotalHours >= 24
            ? string.Create(CultureInfo.InvariantCulture, $"{duration.TotalDays:0.#} days")
            : string.Create(CultureInfo.InvariantCulture, $"{duration.TotalHours:0.#} hours");
}
