using ChartPilot.Core.Manifests;
using ChartPilot.Core.Profiles;
using YamlDotNet.RepresentationModel;

namespace ChartPilot.Core.Checks;

// The CP-SEC-* family: what can this chart do to the cluster, and what can an attacker do with it?
// Every rule here emits Descriptor.DefaultSeverity; the engine resolves the real severity.

/// <summary>CP-SEC-001 — the container is going to run as uid 0.</summary>
internal sealed class RunsAsRootCheck : WorkloadCheckBase
{
    public override CheckDescriptor Descriptor { get; } = new(
        "CP-SEC-001",
        "Container runs as root",
        CheckCategory.Security,
        Severity.Critical,
        "A root process inside a container is one kernel or runtime escape away from being root on the node. It is "
        + "also the precondition for almost every container breakout technique, and almost no application needs it.",
        "securityContext:\n  runAsNonRoot: true\n  runAsUser: 10001\n  runAsGroup: 10001",
        "https://kubernetes.io/docs/tasks/configure-pod-container/security-context/");

    public override IEnumerable<Finding> Evaluate(CheckContext context)
    {
        foreach (var workload in CheckHelpers.Workloads(context))
        {
            foreach (var container in ManifestNavigator.GetContainers(workload))
            {
                var runAsUser = CheckHelpers.SecurityInt(workload, container, "runAsUser");
                var runAsNonRoot = CheckHelpers.SecurityBool(workload, container, "runAsNonRoot");

                if (runAsUser == 0)
                {
                    yield return Violation(
                        workload,
                        $"Container '{container.Name}' sets runAsUser: 0.",
                        container.YamlPath + ".securityContext.runAsUser");
                }
                else if (runAsNonRoot == false)
                {
                    yield return Violation(
                        workload,
                        $"Container '{container.Name}' sets runAsNonRoot: false.",
                        container.YamlPath + ".securityContext.runAsNonRoot");
                }
            }
        }
    }
}

/// <summary>CP-SEC-002 — a privileged container is not a container.</summary>
internal sealed class PrivilegedContainerCheck : WorkloadCheckBase
{
    public override CheckDescriptor Descriptor { get; } = new(
        "CP-SEC-002",
        "Container runs privileged",
        CheckCategory.Security,
        Severity.Critical,
        "privileged: true disables essentially every container isolation boundary — the process gets all "
        + "capabilities and raw device access, so compromising it is equivalent to compromising the node.",
        "securityContext:\n  privileged: false\n  allowPrivilegeEscalation: false\n  capabilities:\n    drop: [ALL]",
        "https://kubernetes.io/docs/concepts/security/pod-security-standards/");

    public override IEnumerable<Finding> Evaluate(CheckContext context)
    {
        foreach (var workload in CheckHelpers.Workloads(context))
        {
            foreach (var container in ManifestNavigator.GetContainers(workload))
            {
                if (ManifestNavigator.GetBool(container.Node, "securityContext.privileged") == true)
                {
                    yield return Violation(
                        workload,
                        $"Container '{container.Name}' sets privileged: true.",
                        container.YamlPath + ".securityContext.privileged");
                }
            }
        }
    }
}

/// <summary>CP-SEC-003 — runAsNonRoot is not declared at all.</summary>
internal sealed class RunAsNonRootDeclaredCheck : WorkloadCheckBase
{
    public override CheckDescriptor Descriptor { get; } = new(
        "CP-SEC-003",
        "Container does not declare runAsNonRoot",
        CheckCategory.Security,
        Severity.Warning,
        "Without runAsNonRoot the effective user is whatever the image happens to declare, so a base-image change "
        + "can silently move a workload back to root. Declaring it makes the kubelet refuse to start a root image.",
        "securityContext:\n  runAsNonRoot: true",
        "https://kubernetes.io/docs/tasks/configure-pod-container/security-context/");

    public override IEnumerable<Finding> Evaluate(CheckContext context)
    {
        foreach (var workload in CheckHelpers.Workloads(context))
        {
            foreach (var container in ManifestNavigator.GetContainers(workload))
            {
                if (CheckHelpers.SecurityBool(workload, container, "runAsNonRoot") is null)
                {
                    yield return Violation(
                        workload,
                        $"Container '{container.Name}' does not declare securityContext.runAsNonRoot.",
                        container.YamlPath + ".securityContext");
                }
            }
        }
    }
}

/// <summary>CP-SEC-004 — a writable root filesystem.</summary>
internal sealed class ReadOnlyRootFilesystemCheck : WorkloadCheckBase
{
    public override CheckDescriptor Descriptor { get; } = new(
        "CP-SEC-004",
        "Container root filesystem is writable",
        CheckCategory.Security,
        Severity.Warning,
        "A writable root filesystem lets an attacker drop tooling, rewrite binaries and persist inside a running "
        + "container. Read-only roots plus an explicit emptyDir for scratch make that class of persistence go away.",
        "securityContext:\n  readOnlyRootFilesystem: true\nvolumeMounts:\n  - name: tmp\n    mountPath: /tmp\n"
        + "volumes:\n  - name: tmp\n    emptyDir: {}",
        "https://kubernetes.io/docs/concepts/security/pod-security-standards/");

    public override IEnumerable<Finding> Evaluate(CheckContext context)
    {
        foreach (var workload in CheckHelpers.Workloads(context))
        {
            foreach (var container in ManifestNavigator.GetContainers(workload))
            {
                if (CheckHelpers.SecurityBool(workload, container, "readOnlyRootFilesystem") != true)
                {
                    yield return Violation(
                        workload,
                        $"Container '{container.Name}' does not set readOnlyRootFilesystem: true.",
                        container.YamlPath + ".securityContext");
                }
            }
        }
    }
}

/// <summary>CP-SEC-005 — an unpinned or moving image tag.</summary>
internal sealed class LatestImageTagCheck : WorkloadCheckBase
{
    public override CheckDescriptor Descriptor { get; } = new(
        "CP-SEC-005",
        "Image tag is latest or missing",
        CheckCategory.Security,
        Severity.Critical,
        "A moving tag means the manifest no longer describes what is running: two pods of the same ReplicaSet can "
        + "be different builds, and a rollback restores the tag rather than the code.",
        "image: ghcr.io/example/my-app:1.4.2   # an immutable tag, or a sha256 digest",
        "https://kubernetes.io/docs/concepts/containers/images/#image-names");

    public override IEnumerable<Finding> Evaluate(CheckContext context)
    {
        foreach (var workload in CheckHelpers.Workloads(context))
        {
            foreach (var container in ManifestNavigator.GetContainers(workload))
            {
                var raw = ManifestNavigator.GetString(container.Node, "image");
                var image = CheckHelpers.ParseImage(raw);

                if (image is null)
                {
                    yield return Violation(
                        workload,
                        $"Container '{container.Name}' declares no image.",
                        container.YamlPath + ".image");
                    continue;
                }

                if (image.HasDigest)
                {
                    continue;
                }

                if (image.Tag is null)
                {
                    yield return Violation(
                        workload,
                        $"Container '{container.Name}' image '{image.Raw}' has no tag, so it resolves to :latest.",
                        container.YamlPath + ".image");
                }
                else if (string.Equals(image.Tag, "latest", StringComparison.OrdinalIgnoreCase))
                {
                    yield return Violation(
                        workload,
                        $"Container '{container.Name}' image '{image.Raw}' uses the latest tag.",
                        container.YamlPath + ".image");
                }
            }
        }
    }
}

/// <summary>CP-SEC-006 — the tag is pinned to something, but not to a version or a digest.</summary>
internal sealed class ImagePinningCheck : WorkloadCheckBase
{
    public override CheckDescriptor Descriptor { get; } = new(
        "CP-SEC-006",
        "Image is not pinned to a digest or a version tag",
        CheckCategory.Security,
        Severity.Warning,
        "Tags such as stable, main or prod are branch pointers: they are rewritten in place, so the same manifest "
        + "renders a different artifact tomorrow. A digest or a semver tag makes a deployment reproducible.",
        "image: ghcr.io/example/my-app@sha256:0f1c...   # or a semver tag such as 1.4.2",
        "https://kubernetes.io/docs/concepts/containers/images/#image-names");

    public override IEnumerable<Finding> Evaluate(CheckContext context)
    {
        foreach (var workload in CheckHelpers.Workloads(context))
        {
            foreach (var container in ManifestNavigator.GetContainers(workload))
            {
                var image = CheckHelpers.ParseImage(ManifestNavigator.GetString(container.Node, "image"));

                // No image or a latest tag is CP-SEC-005's problem, not this rule's.
                if (image is null || image.HasDigest || image.Tag is null || image.IsLatest)
                {
                    continue;
                }

                if (!CheckHelpers.IsVersionLikeTag(image.Tag))
                {
                    yield return Violation(
                        workload,
                        $"Container '{container.Name}' image tag '{image.Tag}' is neither a digest nor a version.",
                        container.YamlPath + ".image");
                }
            }
        }
    }
}

/// <summary>CP-SEC-007 — secret material committed into the chart.</summary>
internal sealed class InlineSecretCheck : CheckBase
{
    public override CheckDescriptor Descriptor { get; } = new(
        "CP-SEC-007",
        "Secret material is inlined in the chart",
        CheckCategory.Security,
        Severity.Critical,
        "A rendered Secret, or a password passed as a literal env var, means the credential lives in git and in "
        + "every CI log that ever rendered the chart. Rotation then requires a code change and a redeploy.",
        "Reference an external secret instead:\n"
        + "env:\n  - name: DB_PASSWORD\n    valueFrom:\n      secretKeyRef:\n        name: my-app-db\n        key: password\n"
        + "and provision my-app-db with External Secrets Operator, Sealed Secrets or the CSI secret store driver.",
        "https://kubernetes.io/docs/concepts/configuration/secret/");

    public override IEnumerable<Finding> Evaluate(CheckContext context)
    {
        foreach (var secret in context.Graph.ByKind("Secret"))
        {
            foreach (var field in new[] { "data", "stringData" })
            {
                if (ManifestNavigator.Get(secret.Root, field) is YamlMappingNode { Children.Count: > 0 } mapping)
                {
                    var keys = mapping.Children.Keys
                        .OfType<YamlScalarNode>()
                        .Select(k => k.Value ?? string.Empty)
                        .Where(k => k.Length > 0)
                        .ToArray();

                    yield return Violation(
                        secret,
                        keys.Length > 0
                            ? $"Secret/{secret.Name} renders inline {field} for: {string.Join(", ", keys)}."
                            : $"Secret/{secret.Name} renders inline {field}.",
                        field);
                }
            }
        }

        foreach (var workload in CheckHelpers.Workloads(context))
        {
            foreach (var container in ManifestNavigator.GetContainers(workload))
            {
                var env = ManifestNavigator.GetSequence(container.Node, "env");

                for (var i = 0; i < env.Count; i++)
                {
                    var name = ManifestNavigator.GetString(env[i], "name");
                    var value = ManifestNavigator.GetString(env[i], "value");

                    if (value is not null && CheckHelpers.LooksLikeSecretName(name))
                    {
                        yield return Violation(
                            workload,
                            $"Container '{container.Name}' passes '{name}' as a literal env var value.",
                            $"{container.YamlPath}.env[{i}]");
                    }
                }
            }
        }
    }
}

/// <summary>CP-SEC-008 — nothing constrains what this workload can talk to.</summary>
internal sealed class NetworkPolicyCheck : LongLivedWorkloadCheckBase
{
    public override CheckDescriptor Descriptor { get; } = new(
        "CP-SEC-008",
        "No NetworkPolicy selects the workload",
        CheckCategory.Security,
        Severity.Warning,
        "A pod with no NetworkPolicy can reach every other pod in the cluster and be reached by them. That is the "
        + "flat network an attacker needs for lateral movement after a single compromised dependency.",
        "apiVersion: networking.k8s.io/v1\nkind: NetworkPolicy\nmetadata:\n  name: my-app\nspec:\n"
        + "  podSelector:\n    matchLabels:\n      app.kubernetes.io/name: my-app\n  policyTypes: [Ingress, Egress]\n"
        + "  ingress:\n    - from:\n        - podSelector:\n            matchLabels:\n              app.kubernetes.io/part-of: my-platform",
        "https://kubernetes.io/docs/concepts/services-networking/network-policies/");

    public override IEnumerable<Finding> Evaluate(CheckContext context)
    {
        foreach (var workload in CheckHelpers.LongLivedWorkloads(context))
        {
            // "selects" is the NetworkPolicy -> workload edge; an empty podSelector selects the whole
            // namespace and therefore produced an edge to every workload.
            if (!CheckHelpers.IsReachedBy(context, workload, GraphRelations.Selects, "NetworkPolicy"))
            {
                yield return Violation(
                    workload,
                    $"No NetworkPolicy selects the pods of {workload.Kind}/{workload.Name}.",
                    "spec.template.metadata.labels");
            }
        }
    }
}

/// <summary>CP-SEC-009 — the API token is mounted into every pod by default.</summary>
internal sealed class ServiceAccountTokenAutomountCheck : WorkloadCheckBase
{
    public override CheckDescriptor Descriptor { get; } = new(
        "CP-SEC-009",
        "ServiceAccount token automount is not disabled",
        CheckCategory.Security,
        Severity.Warning,
        "Kubernetes mounts a usable API token into every pod unless told not to. A workload that never calls the "
        + "API server is handing an attacker cluster credentials for free.",
        "spec:\n  template:\n    spec:\n      automountServiceAccountToken: false\n"
        + "# or, on the ServiceAccount itself:\nautomountServiceAccountToken: false",
        "https://kubernetes.io/docs/tasks/configure-pod-container/configure-service-account/");

    public override IEnumerable<Finding> Evaluate(CheckContext context)
    {
        foreach (var workload in CheckHelpers.Workloads(context))
        {
            var podSpec = ManifestNavigator.GetPodSpec(workload);
            var onPod = ManifestNavigator.GetBool(podSpec, "automountServiceAccountToken");

            if (onPod is not null)
            {
                if (onPod == true)
                {
                    yield return Violation(
                        workload,
                        $"{workload.Kind}/{workload.Name} explicitly sets automountServiceAccountToken: true.",
                        CheckHelpers.PodSpecField(workload, "automountServiceAccountToken"));
                }

                continue;
            }

            var serviceAccountName = ManifestNavigator.GetString(podSpec, "serviceAccountName")
                                     ?? ManifestNavigator.GetString(podSpec, "serviceAccount");

            var serviceAccount = serviceAccountName is null
                ? null
                : context.Graph.Find("ServiceAccount", serviceAccountName);

            var onServiceAccount = serviceAccount is null
                ? null
                : ManifestNavigator.GetBool(serviceAccount.Root, "automountServiceAccountToken");

            if (onServiceAccount != false)
            {
                yield return Violation(
                    workload,
                    $"{workload.Kind}/{workload.Name} does not disable automountServiceAccountToken, "
                    + "so an API token is mounted into every pod.",
                    CheckHelpers.PodSpecField(workload, "automountServiceAccountToken"));
            }
        }
    }
}

/// <summary>CP-SEC-010 — wildcard RBAC.</summary>
internal sealed class BroadRbacCheck : CheckBase, IConditionalCheck
{
    public override CheckDescriptor Descriptor { get; } = new(
        "CP-SEC-010",
        "RBAC rule uses a wildcard",
        CheckCategory.Security,
        Severity.Critical,
        "A wildcard in verbs, resources or apiGroups grants everything the API server will ever add, including "
        + "resources that did not exist when the chart was reviewed. It also usually includes reading Secrets.",
        "rules:\n  - apiGroups: [\"\"]\n    resources: [configmaps]\n    verbs: [get, list, watch]",
        "https://kubernetes.io/docs/reference/access-authn-authz/rbac/");

    public bool IsApplicable(CheckContext context)
        => context.Graph.ByKinds("Role", "ClusterRole").Any();

    public override IEnumerable<Finding> Evaluate(CheckContext context)
    {
        foreach (var role in context.Graph.ByKinds("Role", "ClusterRole"))
        {
            var rules = ManifestNavigator.GetSequence(role.Root, "rules");

            for (var i = 0; i < rules.Count; i++)
            {
                foreach (var field in new[] { "verbs", "resources", "apiGroups" })
                {
                    var values = ManifestNavigator.GetSequence(rules[i], field);

                    var wildcard = values
                        .OfType<YamlScalarNode>()
                        .Any(v => string.Equals(v.Value, "*", StringComparison.Ordinal));

                    if (wildcard)
                    {
                        yield return Violation(
                            role,
                            $"{role.Kind}/{role.Name} rule {i} grants '*' in {field}.",
                            $"rules[{i}].{field}");
                    }
                }
            }
        }
    }
}

/// <summary>CP-SEC-011 — the container may gain privileges it was not started with.</summary>
internal sealed class PrivilegeEscalationCheck : WorkloadCheckBase
{
    public override CheckDescriptor Descriptor { get; } = new(
        "CP-SEC-011",
        "allowPrivilegeEscalation is not false",
        CheckCategory.Security,
        Severity.Warning,
        "Left unset, a process can gain more privileges than its parent through setuid binaries. Setting it to "
        + "false sets no_new_privs on the process and closes the cheapest local escalation path there is.",
        "securityContext:\n  allowPrivilegeEscalation: false",
        "https://kubernetes.io/docs/concepts/security/pod-security-standards/");

    public override IEnumerable<Finding> Evaluate(CheckContext context)
    {
        foreach (var workload in CheckHelpers.Workloads(context))
        {
            foreach (var container in ManifestNavigator.GetContainers(workload))
            {
                if (ManifestNavigator.GetBool(container.Node, "securityContext.allowPrivilegeEscalation") != false)
                {
                    yield return Violation(
                        workload,
                        $"Container '{container.Name}' does not set allowPrivilegeEscalation: false.",
                        container.YamlPath + ".securityContext");
                }
            }
        }
    }
}

/// <summary>CP-SEC-012 — Linux capabilities are not dropped.</summary>
internal sealed class DropCapabilitiesCheck : WorkloadCheckBase
{
    public override CheckDescriptor Descriptor { get; } = new(
        "CP-SEC-012",
        "Container does not drop all Linux capabilities",
        CheckCategory.Security,
        Severity.Warning,
        "The default capability set still includes things like CAP_NET_RAW and CAP_CHOWN, which are useful for ARP "
        + "spoofing and for tampering with mounted files. Dropping ALL and adding back what is needed is the baseline.",
        "securityContext:\n  capabilities:\n    drop: [ALL]\n    # add: [NET_BIND_SERVICE]   # only if you bind < 1024",
        "https://kubernetes.io/docs/concepts/security/pod-security-standards/");

    public override IEnumerable<Finding> Evaluate(CheckContext context)
    {
        foreach (var workload in CheckHelpers.Workloads(context))
        {
            foreach (var container in ManifestNavigator.GetContainers(workload))
            {
                if (!CheckHelpers.DropsAllCapabilities(container))
                {
                    yield return Violation(
                        workload,
                        $"Container '{container.Name}' does not drop ALL capabilities.",
                        container.YamlPath + ".securityContext.capabilities");
                }
            }
        }
    }
}

/// <summary>CP-SEC-013 — the pod shares a host namespace.</summary>
internal sealed class HostNamespaceCheck : WorkloadCheckBase
{
    public override CheckDescriptor Descriptor { get; } = new(
        "CP-SEC-013",
        "Pod shares a host namespace",
        CheckCategory.Security,
        Severity.Critical,
        "hostNetwork, hostPID and hostIPC each remove an isolation boundary: the pod can sniff node traffic, see "
        + "and signal every process on the node, or read other workloads' shared memory.",
        "spec:\n  template:\n    spec:\n      hostNetwork: false\n      hostPID: false\n      hostIPC: false",
        "https://kubernetes.io/docs/concepts/security/pod-security-standards/");

    public override IEnumerable<Finding> Evaluate(CheckContext context)
    {
        foreach (var workload in CheckHelpers.Workloads(context))
        {
            var podSpec = ManifestNavigator.GetPodSpec(workload);

            foreach (var field in new[] { "hostNetwork", "hostPID", "hostIPC" })
            {
                if (ManifestNavigator.GetBool(podSpec, field) == true)
                {
                    yield return Violation(
                        workload,
                        $"{workload.Kind}/{workload.Name} sets {field}: true.",
                        CheckHelpers.PodSpecField(workload, field));
                }
            }
        }
    }
}

/// <summary>CP-SEC-014 — the chart opens a route from outside the cluster where that is not allowed.</summary>
/// <remarks>
/// This is the rule that gives <c>allowPublicIngress: false</c> and <c>platform.exposure</c> teeth. Without it
/// both are declarations nothing enforces: the profile flag only changed the severity of findings that a chart
/// shipping a plain Ingress never produces in the first place.
/// </remarks>
internal sealed class PublicExposureCheck : CheckBase, IConditionalCheck
{
    public override CheckDescriptor Descriptor { get; } = new(
        "CP-SEC-014",
        "Chart exposes a public route that is not allowed",
        CheckCategory.Security,
        Severity.Warning,
        "A route from outside the cluster is the one change that turns every other weakness into an internet-facing "
        + "one. When the profile forbids public ingress, the chart declares itself internal, or the service handles "
        + "sensitive personal data, an Ingress, a public Istio Gateway or a LoadBalancer Service contradicts that.",
        "Expose the service through the internal gateway instead, and let the platform's edge terminate public "
        + "traffic:\nspec:\n  selector:\n    istio: internal-ingressgateway\n"
        + "# Service\nspec:\n  type: ClusterIP\n"
        + "# or state the intent in values.yaml, and pick a profile that allows it:\nplatform:\n  exposure: public",
        "https://kubernetes.io/docs/concepts/services-networking/ingress/");

    /// <summary>Selector values that name an internal-only ingress gateway rather than the public edge.</summary>
    private static readonly string[] InternalMarkers = ["internal", "private", "intranet"];

    public bool IsApplicable(CheckContext context) => ForbiddenBecause(context) is not null;

    public override IEnumerable<Finding> Evaluate(CheckContext context)
    {
        if (ForbiddenBecause(context) is not { } reason)
        {
            yield break;
        }

        foreach (var (resource, path, what) in PublicRoutes(context))
        {
            yield return Violation(
                resource,
                $"{what} accepts traffic from outside the cluster, but {reason}.",
                path);
        }
    }

    /// <summary>
    /// Why a public route is not allowed here, or null when it is. The profile requirement comes first
    /// because it is the platform team's explicit statement; the chart's own declaration and the data
    /// classification are the fallbacks the spec's golden path relies on.
    /// </summary>
    private static string? ForbiddenBecause(CheckContext context)
    {
        if (!context.Profile.Requirements.AllowPublicIngress)
        {
            return $"profile '{context.Profile.Id}' sets allowPublicIngress: false";
        }

        if (context.Exposure == Exposure.Internal)
        {
            return "values.yaml declares platform.exposure: internal";
        }

        if (context.Classification == DataClassification.SensitivePersonalData)
        {
            return "the chart declares platform.dataClassification: sensitive-personal-data, "
                   + "which is not reachable from the public internet";
        }

        return null;
    }

    /// <summary>Every rendered resource that admits traffic originating outside the cluster.</summary>
    private static IEnumerable<(RenderedResource Resource, string Path, string What)> PublicRoutes(CheckContext context)
    {
        foreach (var ingress in context.Graph.ByKind("Ingress"))
        {
            yield return (ingress, "spec.rules", $"Ingress/{ingress.Name}");
        }

        foreach (var service in context.Graph.ByKind("Service"))
        {
            var type = ManifestNavigator.GetString(service.Root, "spec.type");

            if (type is "LoadBalancer" or "NodePort")
            {
                yield return (service, "spec.type", $"Service/{service.Name} of type {type}");
            }
        }

        foreach (var gateway in context.Graph.ByKind("Gateway"))
        {
            // A Gateway nothing routes through is configuration, not exposure: it is the VirtualService
            // binding it (the "binds-gateway" edge) that actually opens the route.
            var bound = context.Graph
                .EdgesTo(gateway.Ref)
                .Any(edge => string.Equals(edge.Relation, GraphRelations.BindsGateway, StringComparison.Ordinal));

            if (bound && IsPublicGateway(gateway))
            {
                yield return (gateway, "spec.selector", $"Gateway/{gateway.Name}");
            }
        }
    }

    /// <summary>
    /// An Istio Gateway is attached to an ingress gateway deployment through its selector. A selector
    /// naming an internal gateway (<c>istio: internal-ingressgateway</c>) keeps the route inside the
    /// perimeter; anything else lands on the cluster's public edge.
    /// </summary>
    private static bool IsPublicGateway(RenderedResource gateway)
    {
        var selector = ManifestNavigator.GetStringMap(gateway.Root, "spec.selector");

        foreach (var value in selector.Values)
        {
            foreach (var marker in InternalMarkers)
            {
                if (value.Contains(marker, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
        }

        return true;
    }
}
