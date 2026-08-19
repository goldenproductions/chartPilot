namespace ChartPilot.Core.Checks.Guidance;

/// <summary>Authored guidance for the CP-NET-* family (Istio and mesh routing).</summary>
internal static class NetworkGuidance
{
    public static IEnumerable<KeyValuePair<string, CheckGuidance>> Entries()
    {
        yield return new("CP-NET-001", new(
            "The VirtualService names a Gateway that this chart does not render. Istio accepts the route without "
            + "complaint, so the install looks clean — but if that Gateway does not exist in the cluster either, "
            + "the route is attached to nothing and the service is simply unreachable.",
            [
                new FixOption(
                    "Ship the Gateway with the chart",
                    "Make the chart self-contained, so installing it produces a working route.",
                    "apiVersion: networking.istio.io/v1\nkind: Gateway\nmetadata:\n  name: member-api-gateway\nspec:\n  selector:\n    istio: internal-ingressgateway\n  servers:\n    - port:\n        number: 443\n        name: https\n        protocol: HTTPS\n      hosts: [member-api.internal.example.com]\n      tls:\n        mode: SIMPLE\n        credentialName: member-api-tls",
                    "Best when the route belongs to this service alone. Every chart owning its own Gateway does "
                    + "mean more Gateways in the cluster, which some platforms discourage.",
                    IsRecommended: true),
                new FixOption(
                    "Point at the shared platform Gateway",
                    "Reference a Gateway the platform team owns, using its full namespaced name.",
                    "spec:\n  gateways:\n    - istio-system/public-ingressgateway",
                    "The common arrangement in a mature platform. Always namespace-qualify it — an unqualified "
                    + "name resolves in the VirtualService's own namespace, which is the usual cause of this "
                    + "finding.",
                    false)
            ]));

        yield return new("CP-NET-002", new(
            "This route is reachable from outside the mesh through a Gateway, and no AuthorizationPolicy covers "
            + "the workload it points at. Anything that reaches the gateway is forwarded to your service — the "
            + "mesh's identity model is present but doing no work.",
            [
                new FixOption(
                    "Require an authenticated caller",
                    "Only forward requests carrying a validated token.",
                    "apiVersion: security.istio.io/v1\nkind: AuthorizationPolicy\nmetadata:\n  name: member-api\nspec:\n  selector:\n    matchLabels:\n      app.kubernetes.io/name: member-api\n  action: ALLOW\n  rules:\n    - from:\n        - source:\n            requestPrincipals: [\"https://accounts.example.com/*\"]",
                    "The right shape for a public API. It needs a RequestAuthentication alongside it to validate "
                    + "the token — an AuthorizationPolicy on its own trusts a principal nothing has verified.",
                    IsRecommended: true),
                new FixOption(
                    "Allow only named service identities",
                    "Restrict callers to specific workloads inside the mesh.",
                    "spec:\n  action: ALLOW\n  rules:\n    - from:\n        - source:\n            principals:\n              - cluster.local/ns/istio-system/sa/internal-ingressgateway\n              - cluster.local/ns/member-platform/sa/member-web",
                    "The strongest option, and the right one for a service-to-service API. Requires strict mTLS to "
                    + "mean anything, since the identity comes from the client certificate.",
                    false),
                new FixOption(
                    "Deny by default, then allow",
                    "Start from a namespace-wide deny so a new workload is closed until someone opens it.",
                    "apiVersion: security.istio.io/v1\nkind: AuthorizationPolicy\nmetadata:\n  name: deny-all\n  namespace: member-platform\nspec:\n  {}    # empty spec = deny everything in the namespace",
                    "The safest posture and the hardest to retrofit: it will break traffic you forgot about. "
                    + "Introduce it in a test namespace first.",
                    false)
            ]));

        yield return new("CP-NET-003", new(
            "No PeerAuthentication sets strict mTLS. Istio's default is PERMISSIVE, which accepts both encrypted "
            + "and plaintext connections — so a client that is not in the mesh keeps working, and nobody "
            + "discovers that this traffic is unencrypted.",
            [
                new FixOption(
                    "Require mTLS for the namespace",
                    "Every workload in the namespace must be talked to over mTLS.",
                    "apiVersion: security.istio.io/v1\nkind: PeerAuthentication\nmetadata:\n  name: default\n  namespace: member-platform\nspec:\n  mtls:\n    mode: STRICT",
                    "The destination. Anything talking plaintext to this namespace breaks the moment it applies, "
                    + "so confirm every client has a sidecar first.",
                    IsRecommended: true),
                new FixOption(
                    "Require it for this workload only",
                    "Narrow the change to the service you are reviewing.",
                    "spec:\n  selector:\n    matchLabels:\n      app.kubernetes.io/name: member-api\n  mtls:\n    mode: STRICT",
                    "A safer first step in a namespace with mixed workloads. Leaves everything else permissive, so "
                    + "it is a staging post rather than an answer.",
                    false),
                new FixOption(
                    "Strict, with an exception for one port",
                    "Keep strict overall while a legacy client is migrated.",
                    "spec:\n  mtls:\n    mode: STRICT\n  portLevelMtls:\n    8080:\n      mode: PERMISSIVE",
                    "Useful during a migration, and honest about where the gap is. Put an expiry on it in your own "
                    + "tracker — port-level exceptions outlive the migrations that justified them.",
                    false)
            ]));

        yield return new("CP-NET-004", new(
            "Traffic is routed to this Service but no DestinationRule configures how. That is where connection "
            + "pooling and outlier detection live, so without one a single slow or half-dead pod keeps receiving "
            + "its share of requests — nothing ejects it from the pool.",
            [
                new FixOption(
                    "Pool connections and eject bad pods",
                    "Bound the concurrency and remove pods that keep failing.",
                    "apiVersion: networking.istio.io/v1\nkind: DestinationRule\nmetadata:\n  name: member-api\nspec:\n  host: member-api\n  trafficPolicy:\n    connectionPool:\n      tcp:\n        maxConnections: 100\n      http:\n        http2MaxRequests: 100\n        maxRequestsPerConnection: 10\n    outlierDetection:\n      consecutive5xxErrors: 5\n      interval: 30s\n      baseEjectionTime: 30s",
                    "The standard resilience configuration, and the one most worth having. Tune the numbers to the "
                    + "service — defaults that are too tight will shed load you could have served.",
                    IsRecommended: true),
                new FixOption(
                    "Outlier detection only",
                    "Take the ejection behaviour without capping concurrency.",
                    "spec:\n  host: member-api\n  trafficPolicy:\n    outlierDetection:\n      consecutive5xxErrors: 5\n      interval: 30s\n      baseEjectionTime: 30s\n      maxEjectionPercent: 50",
                    "Lower risk: it can only remove bad pods, never reject traffic you would have served. A good "
                    + "first step when you do not yet know the right pool sizes.",
                    false)
            ]));

        yield return new("CP-NET-005", new(
            "The route has no timeout, so a request waits as long as the upstream takes. When a dependency gets "
            + "slow, callers hold connections open, their own thread pools fill, and the stall propagates "
            + "backwards through every service in front of this one.",
            [
                new FixOption(
                    "Set a timeout close to the SLO",
                    "Fail fast rather than holding the caller.",
                    "spec:\n  http:\n    - route:\n        - destination:\n            host: member-api\n      timeout: 5s",
                    "Pick a value just above the p99 you actually serve, not a round number. A timeout longer than "
                    + "the caller's own timeout achieves nothing.",
                    IsRecommended: true),
                new FixOption(
                    "Timeout plus bounded retries",
                    "Cap the total time including retry attempts.",
                    "spec:\n  http:\n    - route:\n        - destination:\n            host: member-api\n      timeout: 5s\n      retries:\n        attempts: 3\n        perTryTimeout: 1500ms\n        retryOn: connect-failure,refused-stream,5xx",
                    "Keep perTryTimeout × attempts under the overall timeout, or the outer timeout fires mid-retry "
                    + "and the retries were pointless. Only retry idempotent routes.",
                    false)
            ]));

        yield return new("CP-NET-006", new(
            "The route does not retry. Pods are rescheduled, drained and rolled constantly, and each of those "
            + "produces a brief connection failure that a single retry would have absorbed — instead it surfaces "
            + "as a 502 that somebody has to explain.",
            [
                new FixOption(
                    "Retry the safe failures",
                    "Retry connection-level errors, which are the ones a rollout produces.",
                    "retries:\n  attempts: 3\n  perTryTimeout: 2s\n  retryOn: connect-failure,refused-stream,unavailable",
                    "Safe even for non-idempotent routes: these conditions mean the request never reached the "
                    + "application. Deliberately excludes 5xx, which may mean it did.",
                    IsRecommended: true),
                new FixOption(
                    "Retry 5xx as well",
                    "Also retry server errors, for routes that are genuinely idempotent.",
                    "retries:\n  attempts: 3\n  perTryTimeout: 2s\n  retryOn: connect-failure,refused-stream,5xx",
                    "Only for reads or idempotent writes. On a POST that charges a card, this bills the customer "
                    + "three times.",
                    false)
            ]));

        yield return new("CP-NET-007", new(
            "The route's destination host does not match any Service this chart renders. Kubernetes and Istio "
            + "both accept the configuration, and the mistake only appears at runtime as a 503 with no upstream "
            + "— usually after a rename that was not propagated.",
            [
                new FixOption(
                    "Point at the rendered Service",
                    "Use the Service name the chart actually produces, via the same template helper.",
                    "spec:\n  http:\n    - route:\n        - destination:\n            host: {{ include \"member-api.fullname\" . }}\n            port:\n              number: {{ .Values.service.port }}",
                    "Templating the host from the same helper that names the Service is what stops the two "
                    + "drifting apart the next time the release name changes.",
                    IsRecommended: true),
                new FixOption(
                    "It is deliberately external to this chart",
                    "The destination lives in another namespace or is an external service.",
                    "spec:\n  http:\n    - route:\n        - destination:\n            host: member-db.data.svc.cluster.local\n\n# for a host outside the mesh, declare it:\napiVersion: networking.istio.io/v1\nkind: ServiceEntry\nmetadata:\n  name: payments-api\nspec:\n  hosts: [payments.partner.example.com]\n  ports:\n    - number: 443\n      name: https\n      protocol: HTTPS\n  resolution: DNS",
                    "Use the fully qualified name for a cross-namespace Service, and a ServiceEntry for anything "
                    + "outside the mesh. Both make the intent explicit to the next reader.",
                    false)
            ]));

        yield return new("CP-NET-008", new(
            "The AuthorizationPolicy allows everything. An ALLOW action with no rules — or a rule with no "
            + "constraints — passes any caller, while looking like authorization to a reviewer and satisfying any "
            + "tool that only checks whether a policy exists.",
            [
                new FixOption(
                    "Constrain it to real callers",
                    "Name the identities or the token issuer that should be let through.",
                    "spec:\n  action: ALLOW\n  rules:\n    - from:\n        - source:\n            principals: [\"cluster.local/ns/member-platform/sa/member-web\"]\n      to:\n        - operation:\n            methods: [GET, POST]\n            paths: [\"/api/*\"]",
                    "Constrain the source and the operation. A policy that only names paths still lets any caller "
                    + "in through those paths.",
                    IsRecommended: true),
                new FixOption(
                    "Delete it and rely on default-deny",
                    "An empty-spec deny policy in the namespace is clearer than an allow-all.",
                    "apiVersion: security.istio.io/v1\nkind: AuthorizationPolicy\nmetadata:\n  name: deny-all\n  namespace: member-platform\nspec:\n  {}",
                    "If the allow-all was there to satisfy a checklist, removing it and denying by default is both "
                    + "safer and more honest. Expect to then add the allows you actually need.",
                    false)
            ]));
    }
}
