using ChartPilot.Core.Manifests;
using YamlDotNet.RepresentationModel;

namespace ChartPilot.Core.Checks;

// The CP-NET-* family: the Istio service-mesh surface. These are graph questions — "is this route
// covered by a policy" cannot be answered by looking at one file. Every rule emits
// Descriptor.DefaultSeverity; the engine resolves the real severity.

/// <summary>CP-NET-001 — a VirtualService bound to a Gateway the chart does not ship.</summary>
internal sealed class VirtualServiceGatewayCheck : IstioCheckBase
{
    public override CheckDescriptor Descriptor { get; } = new(
        "CP-NET-001",
        "VirtualService references a Gateway that is not rendered",
        CheckCategory.Security,
        Severity.Warning,
        "A VirtualService bound to a Gateway that does not exist silently routes nothing: the chart installs "
        + "cleanly, the route is accepted, and the service is simply unreachable until somebody notices.",
        "Ship the Gateway with the chart, or bind to the shared one by its fully qualified name:\n"
        + "spec:\n  gateways:\n    - istio-system/public-gateway",
        "https://istio.io/latest/docs/reference/config/networking/virtual-service/");

    public override IEnumerable<Finding> Evaluate(CheckContext context)
    {
        foreach (var virtualService in context.Graph.ByKind("VirtualService"))
        {
            var refs = CheckHelpers.GatewayRefs(virtualService);

            for (var i = 0; i < refs.Count; i++)
            {
                var name = CheckHelpers.ShortName(refs[i]);

                if (context.Graph.Find("Gateway", name) is null)
                {
                    yield return Violation(
                        virtualService,
                        $"VirtualService/{virtualService.Name} binds gateway '{refs[i]}', which this chart does not render.",
                        $"spec.gateways[{i}]");
                }
            }
        }
    }
}

/// <summary>CP-NET-002 — a route reachable from outside the mesh with no authorization in front of it.</summary>
internal sealed class PublicRouteAuthorizationCheck : IstioCheckBase
{
    public override CheckDescriptor Descriptor { get; } = new(
        "CP-NET-002",
        "Public route has no AuthorizationPolicy",
        CheckCategory.Security,
        Severity.Critical,
        "A VirtualService bound to an ingress Gateway is reachable from outside the mesh. Without an "
        + "AuthorizationPolicy the destination workload accepts anything that reaches the gateway, and the mesh's "
        + "identity model is doing no work at all.",
        "apiVersion: security.istio.io/v1\nkind: AuthorizationPolicy\nmetadata:\n  name: my-app\nspec:\n"
        + "  selector:\n    matchLabels:\n      app.kubernetes.io/name: my-app\n  action: ALLOW\n"
        + "  rules:\n    - from:\n        - source:\n            requestPrincipals: [\"*\"]",
        "https://istio.io/latest/docs/reference/config/security/authorization-policy/");

    public override IEnumerable<Finding> Evaluate(CheckContext context)
    {
        // A policy with no selector applies to every workload in the namespace. It is the only kind
        // of policy that can still cover a route whose destination this chart does not render.
        var namespaceWidePolicy = context.Graph
            .ByKind("AuthorizationPolicy")
            .Any(policy => SelectorReader.HasNoSelector(policy));

        foreach (var virtualService in context.Graph.ByKind("VirtualService"))
        {
            if (!CheckHelpers.OutgoingEdges(context, virtualService, GraphRelations.BindsGateway).Any())
            {
                continue; // mesh-internal route
            }

            var workloads = DestinationWorkloads(context, virtualService).ToList();

            // No resolvable destination workload — an external host, a typo, or a Service this chart
            // does not ship. Only a namespace-wide policy can be said to cover it; an unrelated
            // policy that selects some other workload provably does not.
            var covered = workloads.Count == 0
                ? namespaceWidePolicy
                : workloads.All(workload =>
                    CheckHelpers.IsReachedBy(context, workload, GraphRelations.Covers, "AuthorizationPolicy"));

            if (!covered)
            {
                var subject = workloads.Count == 0
                    ? "its destination workload, which this chart does not render"
                    : "its destination workload";

                yield return Violation(
                    virtualService,
                    $"VirtualService/{virtualService.Name} is exposed through a Gateway but no AuthorizationPolicy "
                    + $"covers {subject}.",
                    "spec.gateways");
            }
        }
    }

    /// <summary>
    /// The workloads a public route ultimately reaches: VirtualService -&gt; Service (routes-to) -&gt;
    /// workload (selects), both hops read from the resolved graph edges.
    /// </summary>
    internal static IEnumerable<RenderedResource> DestinationWorkloads(
        CheckContext context,
        RenderedResource virtualService)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var service in CheckHelpers.Targets(context, virtualService, GraphRelations.RoutesTo))
        {
            foreach (var workload in CheckHelpers.WorkloadsBehind(context, service))
            {
                if (seen.Add(workload.Ref.Key))
                {
                    yield return workload;
                }
            }
        }
    }
}

/// <summary>CP-NET-003 — the chart never asks for strict mTLS.</summary>
internal sealed class StrictMtlsCheck : IstioCheckBase
{
    public override CheckDescriptor Descriptor { get; } = new(
        "CP-NET-003",
        "No PeerAuthentication enforces strict mTLS",
        CheckCategory.Security,
        Severity.Warning,
        "Istio's default PERMISSIVE mode accepts plaintext as well as mTLS, so an unencrypted client keeps working "
        + "and nobody finds out. STRICT is what actually makes workload identity a security control.",
        "apiVersion: security.istio.io/v1\nkind: PeerAuthentication\nmetadata:\n  name: default\nspec:\n"
        + "  mtls:\n    mode: STRICT",
        "https://istio.io/latest/docs/reference/config/security/peer_authentication/");

    public override IEnumerable<Finding> Evaluate(CheckContext context)
    {
        var strict = context.Graph
            .ByKind("PeerAuthentication")
            .Any(p => string.Equals(
                ManifestNavigator.GetString(p.Root, "spec.mtls.mode"),
                "STRICT",
                StringComparison.Ordinal));

        if (!strict)
        {
            yield return ChartViolation(
                "The chart renders Istio resources but no PeerAuthentication with mtls.mode: STRICT.");
        }
    }
}

/// <summary>CP-NET-004 — a routed Service with no DestinationRule.</summary>
internal sealed class DestinationRuleCheck : IstioCheckBase
{
    public override CheckDescriptor Descriptor { get; } = new(
        "CP-NET-004",
        "Routed Service has no DestinationRule",
        CheckCategory.Operability,
        Severity.Warning,
        "The DestinationRule is where connection pooling, outlier detection and TLS mode live. Without one a single "
        + "slow backend pod keeps receiving traffic, because nothing ejects it from the load-balancing pool.",
        "apiVersion: networking.istio.io/v1\nkind: DestinationRule\nmetadata:\n  name: my-app\nspec:\n"
        + "  host: my-app\n  trafficPolicy:\n    connectionPool:\n      http:\n        http2MaxRequests: 100\n"
        + "    outlierDetection:\n      consecutive5xxErrors: 5\n      interval: 30s\n      baseEjectionTime: 30s",
        "https://istio.io/latest/docs/reference/config/networking/destination-rule/");

    public override IEnumerable<Finding> Evaluate(CheckContext context)
    {
        foreach (var virtualService in context.Graph.ByKind("VirtualService"))
        {
            var reported = new HashSet<string>(StringComparer.Ordinal);

            foreach (var (host, path) in CheckHelpers.DestinationHosts(virtualService))
            {
                var name = CheckHelpers.ShortName(host);

                if (context.Graph.Find("Service", name) is not { } service || !reported.Add(name))
                {
                    continue;
                }

                // "applies-to" is the DestinationRule -> Service edge the graph builder resolved from
                // spec.host, short name and fully qualified form alike.
                if (CheckHelpers.IsReachedBy(context, service, GraphRelations.AppliesTo, "DestinationRule"))
                {
                    continue;
                }

                yield return Violation(
                    virtualService,
                    $"Service/{name} is routed to by VirtualService/{virtualService.Name} but has no DestinationRule.",
                    path);
            }
        }
    }
}

/// <summary>CP-NET-005 — an HTTP route with no timeout.</summary>
internal sealed class RouteTimeoutCheck : IstioCheckBase
{
    public override CheckDescriptor Descriptor { get; } = new(
        "CP-NET-005",
        "HTTP route has no timeout",
        CheckCategory.Reliability,
        Severity.Warning,
        "Without an explicit timeout a stuck upstream holds the caller's connection open until its own timeout "
        + "fires. That is how one slow dependency turns into thread-pool exhaustion three services upstream.",
        "spec:\n  http:\n    - route:\n        - destination:\n            host: my-app\n      timeout: 5s",
        "https://istio.io/latest/docs/tasks/traffic-management/request-timeouts/");

    public override bool IsApplicable(CheckContext context) => context.Graph.ContainsKind("VirtualService");

    public override IEnumerable<Finding> Evaluate(CheckContext context)
    {
        foreach (var virtualService in context.Graph.ByKind("VirtualService"))
        {
            foreach (var (route, path, protocol) in CheckHelpers.Routes(virtualService))
            {
                if (protocol != "http")
                {
                    continue;
                }

                if (ManifestNavigator.Get(route, "timeout") is null)
                {
                    yield return Violation(
                        virtualService,
                        $"Route {path} of VirtualService/{virtualService.Name} has no timeout.",
                        path);
                }
            }
        }
    }
}

/// <summary>CP-NET-006 — an HTTP route with no retry policy.</summary>
internal sealed class RouteRetryCheck : IstioCheckBase
{
    public override CheckDescriptor Descriptor { get; } = new(
        "CP-NET-006",
        "HTTP route has no retry policy",
        CheckCategory.Reliability,
        Severity.Info,
        "Pods get rescheduled, drained and rolled constantly. A small, bounded retry budget at the mesh turns those "
        + "routine single-pod blips into nothing, instead of into 502s the application team has to explain.",
        "spec:\n  http:\n    - route:\n        - destination:\n            host: my-app\n      retries:\n"
        + "        attempts: 3\n        perTryTimeout: 2s\n        retryOn: connect-failure,refused-stream,5xx",
        "https://istio.io/latest/docs/concepts/traffic-management/#retries");

    public override bool IsApplicable(CheckContext context) => context.Graph.ContainsKind("VirtualService");

    public override IEnumerable<Finding> Evaluate(CheckContext context)
    {
        foreach (var virtualService in context.Graph.ByKind("VirtualService"))
        {
            foreach (var (route, path, protocol) in CheckHelpers.Routes(virtualService))
            {
                if (protocol != "http")
                {
                    continue;
                }

                if (ManifestNavigator.Get(route, "retries") is null)
                {
                    yield return Violation(
                        virtualService,
                        $"Route {path} of VirtualService/{virtualService.Name} has no retry policy.",
                        path);
                }
            }
        }
    }
}

/// <summary>CP-NET-007 — a route whose destination host resolves to nothing.</summary>
internal sealed class DanglingRouteCheck : IstioCheckBase
{
    public override CheckDescriptor Descriptor { get; } = new(
        "CP-NET-007",
        "Route destination resolves to no rendered Service",
        CheckCategory.Reliability,
        Severity.Warning,
        "A typo in destination.host is accepted by the API server and only shows up as 503 NR at runtime. Checking "
        + "it against the Services the chart actually renders catches the rename that nobody propagated.",
        "spec:\n  http:\n    - route:\n        - destination:\n            host: my-app   # must match a rendered Service\n"
        + "# For a genuinely external destination, declare a ServiceEntry for the host.",
        "https://istio.io/latest/docs/reference/config/networking/virtual-service/#Destination");

    public override bool IsApplicable(CheckContext context) => context.Graph.ContainsKind("VirtualService");

    public override IEnumerable<Finding> Evaluate(CheckContext context)
    {
        var serviceEntryHosts = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entry in context.Graph.ByKind("ServiceEntry"))
        {
            foreach (var host in ManifestNavigator.GetSequence(entry.Root, "spec.hosts").OfType<YamlScalarNode>())
            {
                if (host.Value is { Length: > 0 } value)
                {
                    serviceEntryHosts.Add(CheckHelpers.ShortName(value));
                }
            }
        }

        foreach (var virtualService in context.Graph.ByKind("VirtualService"))
        {
            foreach (var (host, path) in CheckHelpers.DestinationHosts(virtualService))
            {
                var name = CheckHelpers.ShortName(host);

                if (context.Graph.Find("Service", name) is null && !serviceEntryHosts.Contains(name))
                {
                    yield return Violation(
                        virtualService,
                        $"Destination host '{host}' matches no Service or ServiceEntry rendered by this chart.",
                        path);
                }
            }
        }
    }
}

/// <summary>CP-NET-008 — an AuthorizationPolicy that authorizes nothing in particular.</summary>
internal sealed class AllowAllAuthorizationPolicyCheck : IstioCheckBase
{
    public override CheckDescriptor Descriptor { get; } = new(
        "CP-NET-008",
        "AuthorizationPolicy allows all traffic",
        CheckCategory.Security,
        Severity.Critical,
        "An ALLOW policy with no constraints is worse than no policy: it looks like authorization in a review, and "
        + "it satisfies any tooling that only checks whether a policy exists, while permitting every caller.",
        "spec:\n  action: ALLOW\n  rules:\n    - from:\n        - source:\n            principals:\n"
        + "            - cluster.local/ns/gateways/sa/istio-ingressgateway\n      to:\n        - operation:\n"
        + "            methods: [GET, POST]",
        "https://istio.io/latest/docs/reference/config/security/authorization-policy/");

    public override bool IsApplicable(CheckContext context) => context.Graph.ContainsKind("AuthorizationPolicy");

    public override IEnumerable<Finding> Evaluate(CheckContext context)
    {
        foreach (var policy in context.Graph.ByKind("AuthorizationPolicy"))
        {
            var action = ManifestNavigator.GetString(policy.Root, "spec.action") ?? "ALLOW";

            if (!string.Equals(action, "ALLOW", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var rules = ManifestNavigator.GetSequence(policy.Root, "spec.rules");

            // An ALLOW policy with no rules is Istio's canonical deny-all: it allows nothing, so it
            // is the strictest possible policy rather than a finding. Only a rule that exists and
            // constrains nothing actually admits every caller.
            if (rules.Count == 0)
            {
                continue;
            }

            for (var i = 0; i < rules.Count; i++)
            {
                var unconstrained = ManifestNavigator.Get(rules[i], "from") is null
                                    && ManifestNavigator.Get(rules[i], "to") is null
                                    && ManifestNavigator.Get(rules[i], "when") is null;

                if (unconstrained)
                {
                    yield return Violation(
                        policy,
                        $"AuthorizationPolicy/{policy.Name} rule {i} has no from, to or when constraint, so it allows every caller.",
                        $"spec.rules[{i}]");
                }
            }
        }
    }
}
