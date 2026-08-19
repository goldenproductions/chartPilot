using YamlDotNet.RepresentationModel;

namespace ChartPilot.Core.Manifests;

/// <summary>
/// Builds an <see cref="IResourceGraph"/> from a rendered manifest set, resolving the cross-references that make
/// graph-level checks (a public route without an authorization policy, a dangling TLS secret) possible.
/// </summary>
public interface IResourceGraphBuilder
{
    IResourceGraph Build(IReadOnlyList<RenderedResource> resources);
}

/// <summary>
/// The default <see cref="IResourceGraphBuilder"/>.
/// </summary>
/// <remarks>
/// Edges are emitted even when the target is not part of the render: a dangling reference is exactly what the
/// <c>CP-CERT-*</c> and <c>CP-NET-*</c> rules want to report, and an unresolved target is recognised by
/// <see cref="IResourceGraph.Resolve"/> returning <c>null</c>. Edges are de-duplicated and sorted by
/// (from key, relation, to key) so report snapshots are stable.
/// </remarks>
public sealed class ResourceGraphBuilder : IResourceGraphBuilder
{
    private const string ClusterLocalSuffix = ".svc.cluster.local";

    /// <summary>Kinds whose selector covers workloads namespace-wide when the selector is absent.</summary>
    private static readonly string[] CoveringKinds = ["AuthorizationPolicy", "PeerAuthentication", "Sidecar"];

    public IResourceGraph Build(IReadOnlyList<RenderedResource> resources)
    {
        var all = resources ?? Array.Empty<RenderedResource>();

        // Two phases: the edge resolution needs kind/name lookups and selector matching, both of which the
        // graph already implements. Building it twice is cheap and keeps the graph immutable.
        var index = new ResourceGraph(all, Array.Empty<GraphEdge>());
        var edges = new List<GraphEdge>();

        foreach (var resource in all)
        {
            switch (resource.Kind)
            {
                case "VirtualService":
                    AddVirtualServiceEdges(index, resource, edges);
                    break;
                case "DestinationRule":
                    AddDestinationRuleEdges(index, resource, edges);
                    break;
                case "Certificate":
                    AddCertificateEdges(index, resource, edges);
                    break;
                case "Service":
                    AddServiceEdges(index, resource, edges);
                    break;
                case "NetworkPolicy":
                    AddSelectorEdges(index, resource, GraphRelations.Selects, edges);
                    break;
                case "PodDisruptionBudget":
                    AddSelectorEdges(index, resource, GraphRelations.TargetsWorkload, edges);
                    break;
                case "HorizontalPodAutoscaler":
                    AddHpaEdges(index, resource, edges);
                    break;
            }

            if (CoveringKinds.Contains(resource.Kind, StringComparer.Ordinal))
            {
                AddSelectorEdges(index, resource, GraphRelations.Covers, edges);
            }

            if (ResourceGraph.WorkloadKinds.Contains(resource.Kind))
            {
                AddWorkloadEdges(index, resource, edges);
            }
        }

        return new ResourceGraph(all, Normalize(edges));
    }

    // ---------------------------------------------------------------- Istio

    private static void AddVirtualServiceEdges(IResourceGraph index, RenderedResource vs, List<GraphEdge> edges)
    {
        foreach (var section in new[] { "spec.http", "spec.tcp", "spec.tls" })
        {
            foreach (var route in ManifestNavigator.GetSequence(vs.Root, section))
            {
                foreach (var hop in ManifestNavigator.GetSequence(route, "route"))
                {
                    var name = ServiceNameFromHost(ManifestNavigator.GetString(hop, "destination.host"));
                    if (name is not null)
                    {
                        edges.Add(Edge(index, vs, "Service", name, GraphRelations.RoutesTo));
                    }
                }
            }
        }

        foreach (var gateway in ManifestNavigator.GetSequence(vs.Root, "spec.gateways"))
        {
            if (gateway is not YamlScalarNode { Value: { } raw })
            {
                continue;
            }

            var value = raw.Trim();

            // "mesh" is the reserved sidecar mesh gateway, not a Gateway resource.
            if (value.Length == 0 || string.Equals(value, "mesh", StringComparison.Ordinal))
            {
                continue;
            }

            var slash = value.LastIndexOf('/');
            var name = slash >= 0 ? value[(slash + 1)..] : value;

            if (name.Length > 0)
            {
                edges.Add(Edge(index, vs, "Gateway", name, GraphRelations.BindsGateway));
            }
        }
    }

    private static void AddDestinationRuleEdges(IResourceGraph index, RenderedResource rule, List<GraphEdge> edges)
    {
        var name = ServiceNameFromHost(ManifestNavigator.GetString(rule.Root, "spec.host"));
        if (name is not null)
        {
            edges.Add(Edge(index, rule, "Service", name, GraphRelations.AppliesTo));
        }
    }

    // ---------------------------------------------------------- cert-manager

    private static void AddCertificateEdges(IResourceGraph index, RenderedResource certificate, List<GraphEdge> edges)
    {
        var issuer = ManifestNavigator.GetString(certificate.Root, "spec.issuerRef.name");
        if (!string.IsNullOrEmpty(issuer))
        {
            var kind = ManifestNavigator.GetString(certificate.Root, "spec.issuerRef.kind");
            if (string.IsNullOrEmpty(kind))
            {
                kind = "Issuer";
            }

            edges.Add(Edge(index, certificate, kind, issuer, GraphRelations.IssuedBy));
        }

        var secret = ManifestNavigator.GetString(certificate.Root, "spec.secretName");
        if (!string.IsNullOrEmpty(secret))
        {
            edges.Add(Edge(index, certificate, "Secret", secret, GraphRelations.WritesSecret));
        }
    }

    // ------------------------------------------------------------- workloads

    private static void AddWorkloadEdges(IResourceGraph index, RenderedResource workload, List<GraphEdge> edges)
    {
        var podSpec = ManifestNavigator.GetPodSpec(workload);
        if (podSpec is null)
        {
            return;
        }

        var serviceAccount = ManifestNavigator.GetString(podSpec, "serviceAccountName");
        if (!string.IsNullOrEmpty(serviceAccount))
        {
            edges.Add(Edge(index, workload, "ServiceAccount", serviceAccount, GraphRelations.UsesServiceAccount));
        }

        foreach (var container in ManifestNavigator.GetContainers(workload))
        {
            foreach (var envFrom in ManifestNavigator.GetSequence(container.Node, "envFrom"))
            {
                AddIfPresent(index, workload, "Secret", ManifestNavigator.GetString(envFrom, "secretRef.name"),
                    GraphRelations.MountsSecret, edges);
                AddIfPresent(index, workload, "ConfigMap", ManifestNavigator.GetString(envFrom, "configMapRef.name"),
                    GraphRelations.MountsConfigMap, edges);
            }

            foreach (var env in ManifestNavigator.GetSequence(container.Node, "env"))
            {
                AddIfPresent(index, workload, "Secret", ManifestNavigator.GetString(env, "valueFrom.secretKeyRef.name"),
                    GraphRelations.MountsSecret, edges);
                AddIfPresent(index, workload, "ConfigMap", ManifestNavigator.GetString(env, "valueFrom.configMapKeyRef.name"),
                    GraphRelations.MountsConfigMap, edges);
            }
        }

        foreach (var volume in ManifestNavigator.GetSequence(podSpec, "volumes"))
        {
            AddIfPresent(index, workload, "Secret", ManifestNavigator.GetString(volume, "secret.secretName"),
                GraphRelations.MountsSecret, edges);
            AddIfPresent(index, workload, "ConfigMap", ManifestNavigator.GetString(volume, "configMap.name"),
                GraphRelations.MountsConfigMap, edges);
        }
    }

    // -------------------------------------------------------------- selectors

    /// <summary>
    /// A Service without a selector (headless, ExternalName, or manually managed endpoints) selects no pods,
    /// so it produces no edges — unlike a policy resource, where an empty selector means namespace-wide.
    /// </summary>
    private static void AddServiceEdges(IResourceGraph index, RenderedResource service, List<GraphEdge> edges)
    {
        var selector = SelectorReader.SelectorOf(service);
        if (selector.Count == 0)
        {
            return;
        }

        foreach (var workload in index.WorkloadsMatchedBySelector(selector))
        {
            edges.Add(new GraphEdge(service.Ref, workload.Ref, GraphRelations.Selects));
        }
    }

    private static void AddSelectorEdges(IResourceGraph index, RenderedResource source, string relation, List<GraphEdge> edges)
    {
        // An absent selector is namespace-wide for NetworkPolicy, PodDisruptionBudget and the Istio policies,
        // so every workload is covered.
        var selector = SelectorReader.SelectorOf(source);

        foreach (var workload in index.WorkloadsMatchedBySelector(selector))
        {
            edges.Add(new GraphEdge(source.Ref, workload.Ref, relation));
        }
    }

    private static void AddHpaEdges(IResourceGraph index, RenderedResource hpa, List<GraphEdge> edges)
    {
        var kind = ManifestNavigator.GetString(hpa.Root, "spec.scaleTargetRef.kind");
        var name = ManifestNavigator.GetString(hpa.Root, "spec.scaleTargetRef.name");

        if (!string.IsNullOrEmpty(kind) && !string.IsNullOrEmpty(name))
        {
            edges.Add(Edge(index, hpa, kind, name, GraphRelations.TargetsWorkload));
        }
    }

    // ----------------------------------------------------------------- helpers

    private static void AddIfPresent(
        IResourceGraph index,
        RenderedResource from,
        string kind,
        string? name,
        string relation,
        List<GraphEdge> edges)
    {
        if (!string.IsNullOrEmpty(name))
        {
            edges.Add(Edge(index, from, kind, name, relation));
        }
    }

    /// <summary>
    /// Builds an edge to <paramref name="kind"/>/<paramref name="name"/>. When the target is part of the render
    /// its namespace is carried over; otherwise the reference stays unresolved on purpose.
    /// </summary>
    private static GraphEdge Edge(IResourceGraph index, RenderedResource from, string kind, string name, string relation)
    {
        var target = index.Find(kind, name);
        var reference = target is not null ? target.Ref : new ResourceRef(kind, name);
        return new GraphEdge(from.Ref, reference, relation);
    }

    /// <summary>
    /// The Kubernetes service name behind an Istio host: <c>member-api.prod.svc.cluster.local</c>,
    /// <c>member-api.prod</c> and <c>member-api</c> all resolve to <c>member-api</c>. Wildcards resolve to nothing.
    /// </summary>
    private static string? ServiceNameFromHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return null;
        }

        var value = host.Trim().TrimEnd('.');

        if (value.EndsWith(ClusterLocalSuffix, StringComparison.Ordinal))
        {
            value = value[..^ClusterLocalSuffix.Length];
        }

        if (value.Length == 0 || value.StartsWith('*'))
        {
            return null;
        }

        var dot = value.IndexOf('.');
        var name = dot >= 0 ? value[..dot] : value;

        return name.Length == 0 ? null : name;
    }

    /// <summary>De-duplicates and orders the edges so two runs over the same manifests produce identical output.</summary>
    private static IReadOnlyList<GraphEdge> Normalize(List<GraphEdge> edges)
        => edges
            .GroupBy(e => (e.From.Key, e.Relation, e.To.Key))
            .Select(g => g.First())
            .OrderBy(e => e.From.Key, StringComparer.Ordinal)
            .ThenBy(e => e.Relation, StringComparer.Ordinal)
            .ThenBy(e => e.To.Key, StringComparer.Ordinal)
            .ToList();
}
