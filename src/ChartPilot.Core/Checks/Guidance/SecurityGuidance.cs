namespace ChartPilot.Core.Checks.Guidance;

/// <summary>Authored guidance for the CP-SEC-* family.</summary>
internal static class SecurityGuidance
{
    public static IEnumerable<KeyValuePair<string, CheckGuidance>> Entries()
    {
        yield return new("CP-SEC-001", new(
            "The container starts its process as uid 0 — root inside the container. Container root is not the "
            + "same as node root, but it is one runtime or kernel bug away from it, and it is what almost every "
            + "container escape needs as a starting point.",
            [
                new FixOption(
                    "Run as a fixed non-root user",
                    "Pick a high uid and tell Kubernetes to refuse the image if it would run as root.",
                    "securityContext:\n  runAsNonRoot: true\n  runAsUser: 10001\n  runAsGroup: 10001\n  fsGroup: 10001",
                    "The right answer for almost every application. If the image writes to paths owned by root at "
                    + "build time, you will need to chown them in the Dockerfile or mount them as volumes.",
                    IsRecommended: true),
                new FixOption(
                    "Use the uid the image already declares",
                    "Keep the image's own USER and only assert that it is not root.",
                    "securityContext:\n  runAsNonRoot: true",
                    "Less to configure and it survives an image that changes its uid. But if a future base image "
                    + "drops back to root, the pod stops starting rather than starting insecurely — which is the "
                    + "correct failure, though it will surprise someone.",
                    false),
                new FixOption(
                    "The process genuinely needs a privileged port",
                    "Stay non-root and grant the one capability that binding below port 1024 requires.",
                    "securityContext:\n  runAsNonRoot: true\n  runAsUser: 10001\n  capabilities:\n    drop: [ALL]\n    add: [NET_BIND_SERVICE]",
                    "Only for a process that must listen on 80 or 443 inside the pod. Changing the container to "
                    + "listen on 8080 and letting the Service map the port is simpler and strictly safer.",
                    false)
            ]));

        yield return new("CP-SEC-002", new(
            "privileged: true switches off container isolation. The process gets every Linux capability and raw "
            + "access to the node's devices, so anything that compromises this container has effectively "
            + "compromised the node it runs on.",
            [
                new FixOption(
                    "Turn it off",
                    "Almost no application workload needs privileged mode; it is usually copied from an example.",
                    "securityContext:\n  privileged: false\n  allowPrivilegeEscalation: false\n  capabilities:\n    drop: [ALL]",
                    "Try this first. If the container still works, it never needed privilege — which is the common "
                    + "case.",
                    IsRecommended: true),
                new FixOption(
                    "Grant only the capability it actually needs",
                    "Replace blanket privilege with the specific kernel capability the workload uses.",
                    "securityContext:\n  privileged: false\n  capabilities:\n    drop: [ALL]\n    add: [NET_ADMIN]   # or SYS_TIME, IPC_LOCK - name the one you need",
                    "Requires knowing which capability the process needs; run it without privilege and read the "
                    + "permission error. Far narrower than privileged, and reviewable.",
                    false),
                new FixOption(
                    "It really is a node agent",
                    "Some workloads — CNI plugins, node exporters, storage drivers — legitimately need this.",
                    "# Keep privileged: true, and record why:\n# .chartpilot.yaml\nsuppress:\n  - id: CP-SEC-002\n    resource: DaemonSet/node-agent\n    reason: \"CNI plugin, needs raw device access. Approved by platform team, ticket PLAT-88.\"\n    expires: 2027-01-01",
                    "Only for infrastructure workloads, not application ones. The expiry forces the exception back "
                    + "onto someone's desk instead of becoming permanent.",
                    false)
            ]));

        yield return new("CP-SEC-003", new(
            "Nothing in the manifest asserts that this container runs as a non-root user. It may well be running "
            + "as a normal user today because the image says so — but that is the image's decision, and a base "
            + "image update can silently change it.",
            [
                new FixOption(
                    "Assert it at the pod level",
                    "Set it once for every container in the pod, including ones added later.",
                    "spec:\n  template:\n    spec:\n      securityContext:\n        runAsNonRoot: true\n        runAsUser: 10001\n        fsGroup: 10001",
                    "The best default: a container added to this pod next year inherits it automatically. "
                    + "A container that overrides it at its own level still wins, so check both.",
                    IsRecommended: true),
                new FixOption(
                    "Assert it on the container",
                    "Set it on this specific container, leaving siblings alone.",
                    "containers:\n  - name: app\n    securityContext:\n      runAsNonRoot: true\n      runAsUser: 10001",
                    "Use when one container in the pod genuinely differs. It does not protect containers added "
                    + "later, so it needs remembering.",
                    false)
            ]));

        yield return new("CP-SEC-004", new(
            "The container can write anywhere inside its own filesystem. That is how an attacker who gets code "
            + "execution turns a transient exploit into something persistent — they drop a binary and it survives.",
            [
                new FixOption(
                    "Make the root filesystem read-only",
                    "The container can still read everything; it just cannot modify its own image.",
                    "securityContext:\n  readOnlyRootFilesystem: true",
                    "Try this first. If the application only reads config and writes logs to stdout, it works "
                    + "unchanged. If it crashes on start, it needs somewhere to write — see the next option.",
                    IsRecommended: true),
                new FixOption(
                    "Read-only, with scratch space",
                    "Lock the image down and mount a writable, in-memory directory where it needs one.",
                    "securityContext:\n  readOnlyRootFilesystem: true\nvolumeMounts:\n  - name: tmp\n    mountPath: /tmp\nvolumes:\n  - name: tmp\n    emptyDir: {}",
                    "The usual answer for applications that buffer uploads, render templates or write a pid file. "
                    + "emptyDir is wiped on restart, which is what you want — anything that must survive belongs "
                    + "in a real volume.",
                    false),
                new FixOption(
                    "Accept it, with an expiry date",
                    "Record the exception rather than leaving the finding unexplained.",
                    "# .chartpilot.yaml\nsuppress:\n  - id: CP-SEC-004\n    resource: Deployment/legacy-importer\n    reason: \"Vendor image writes to /var/lib at runtime; upstream issue #412.\"\n    expires: 2026-12-01",
                    "For a third-party image you cannot change. The waiver expires and the finding comes back, so "
                    + "it stays a decision rather than becoming invisible.",
                    false)
            ]));

        yield return new("CP-SEC-005", new(
            "The image reference points at a moving tag. `latest` — or no tag at all — means the image that runs "
            + "tomorrow can differ from the one you tested today, and a rollback does not actually roll anything "
            + "back, because the tag has moved underneath you.",
            [
                new FixOption(
                    "Pin to an immutable version tag",
                    "Reference a release the registry will never reassign.",
                    "image:\n  repository: ghcr.io/example/member-api\n  tag: \"1.12.0\"\n  pullPolicy: IfNotPresent",
                    "The normal answer, and it keeps the values file readable. It relies on your registry not "
                    + "allowing tags to be overwritten — most CI setups enforce that.",
                    IsRecommended: true),
                new FixOption(
                    "Pin to a digest",
                    "Reference the exact image bytes, which cannot be reassigned at all.",
                    "image:\n  repository: ghcr.io/example/member-api\n  digest: sha256:5b8f9c2e1d4a7b3c6e0f9a2d5c8b1e4f7a0d3c6b9e2f5a8d1c4b7e0f3a6d9c2b",
                    "The strongest guarantee, and what you want for anything security-sensitive. The cost is "
                    + "readability: nobody can tell which version is deployed by looking at the values file.",
                    false)
            ]));

        yield return new("CP-SEC-006", new(
            "The image tag is not obviously a version. It is not `latest`, so it is not the worst case, but a tag "
            + "like `stable`, `prod` or a branch name is still a pointer somebody can move without changing this "
            + "chart.",
            [
                new FixOption(
                    "Use the release version",
                    "Tag images with the version the application actually reports.",
                    "image:\n  tag: \"1.12.0\"",
                    "Makes the deployed version legible in `kubectl describe`, in this chart, and in an incident. "
                    + "Requires CI to tag by version, which is a one-line pipeline change.",
                    IsRecommended: true),
                new FixOption(
                    "Use the commit sha",
                    "Tag with the build's git sha when there is no meaningful version number.",
                    "image:\n  tag: \"git-4f3a91c\"",
                    "Good for services that deploy continuously and have no release cadence. Traceable back to a "
                    + "commit, though less readable than a version.",
                    false)
            ]));

        yield return new("CP-SEC-007", new(
            "Secret values are written directly into the chart, so they are in your git history, in every "
            + "checkout, in CI logs that echo the rendered manifest, and readable by anyone who can read the "
            + "repository — which is a much larger group than the people who should hold the credential.",
            [
                new FixOption(
                    "Reference an externally managed secret",
                    "Let the chart name a Secret that something else creates and rotates.",
                    "env:\n  - name: DB_PASSWORD\n    valueFrom:\n      secretKeyRef:\n        name: member-api-db\n        key: password",
                    "The standard answer. The Secret comes from External Secrets Operator, Sealed Secrets, the CSI "
                    + "secret store driver or your platform's own mechanism — the chart stops carrying the value.",
                    IsRecommended: true),
                new FixOption(
                    "Ship an encrypted secret with the chart",
                    "Keep the secret in git, but encrypted so only the cluster can read it.",
                    "apiVersion: bitnami.com/v1alpha1\nkind: SealedSecret\nmetadata:\n  name: member-api-db\nspec:\n  encryptedData:\n    password: AgBv1x...   # sealed with the cluster's public key",
                    "Keeps the GitOps property that the repository describes the whole system. Needs the Sealed "
                    + "Secrets controller in the cluster, and re-sealing when the cluster key rotates.",
                    false),
                new FixOption(
                    "Inject at deploy time",
                    "Pass the value from your CI system's secret store, never committing it.",
                    "# helm upgrade --install member-api ./chart \\\n#   --set-string database.password=\"$DB_PASSWORD\"\n\n# and in values.yaml, leave it empty:\ndatabase:\n  password: \"\"",
                    "Simple and needs no cluster components. The downside is that the deployed state is no longer "
                    + "fully described by the repository, and the value lives in your CI provider instead.",
                    false)
            ]));

        yield return new("CP-SEC-008", new(
            "No NetworkPolicy selects this workload's pods. In a default Kubernetes cluster that means every pod "
            + "in every namespace can open a connection to it, and it can reach anything — so a foothold anywhere "
            + "in the cluster is a foothold next to this service.",
            [
                new FixOption(
                    "Default-deny, then allow what is needed",
                    "Close the workload off and name the callers it actually serves.",
                    "apiVersion: networking.k8s.io/v1\nkind: NetworkPolicy\nmetadata:\n  name: member-api\nspec:\n  podSelector:\n    matchLabels:\n      app.kubernetes.io/name: member-api\n  policyTypes: [Ingress, Egress]\n  ingress:\n    - from:\n        - namespaceSelector:\n            matchLabels:\n              kubernetes.io/metadata.name: istio-system\n  egress:\n    - to:\n        - podSelector:\n            matchLabels:\n              app.kubernetes.io/name: member-db",
                    "The real answer, and the one that survives review. It takes knowing the traffic the service "
                    + "sends and receives — start by allowing what you know and watch for what breaks in a test "
                    + "environment.",
                    IsRecommended: true),
                new FixOption(
                    "Ingress-only to start",
                    "Restrict who can reach it, and leave outbound alone for now.",
                    "spec:\n  podSelector:\n    matchLabels:\n      app.kubernetes.io/name: member-api\n  policyTypes: [Ingress]\n  ingress:\n    - from:\n        - podSelector:\n            matchLabels:\n              app.kubernetes.io/part-of: member-platform",
                    "A good staging post when you do not yet know the egress. It stops lateral movement toward the "
                    + "service without risking an outage from a blocked outbound call.",
                    false),
                new FixOption(
                    "The platform provides it",
                    "Some clusters apply a namespace-wide default-deny that the chart does not render.",
                    "# .chartpilot.yaml\nsuppress:\n  - id: CP-SEC-008\n    reason: \"Namespace-wide default-deny applied by the platform baseline chart.\"\n    expires: 2027-06-01",
                    "Legitimate, and common in a mature platform. Confirm the policy really exists in the target "
                    + "namespace before waiving — this is a comfortable assumption to be wrong about.",
                    false)
            ]));

        yield return new("CP-SEC-009", new(
            "Kubernetes mounts a ServiceAccount token into the pod by default. If the workload never calls the "
            + "Kubernetes API — and most application workloads never do — that token is a credential sitting on "
            + "disk for no reason, waiting for a path traversal or an SSRF to read it.",
            [
                new FixOption(
                    "Turn it off on the pod",
                    "Stop mounting the token for this workload.",
                    "spec:\n  template:\n    spec:\n      automountServiceAccountToken: false",
                    "Right for any workload that does not talk to the Kubernetes API. If something does break, it "
                    + "breaks loudly and immediately, which makes this safe to try.",
                    IsRecommended: true),
                new FixOption(
                    "Turn it off on the ServiceAccount",
                    "Apply the default to everything that uses this ServiceAccount.",
                    "apiVersion: v1\nkind: ServiceAccount\nmetadata:\n  name: member-api\nautomountServiceAccountToken: false",
                    "Covers pods added later. A pod can still opt back in explicitly, so this is a default rather "
                    + "than a guarantee.",
                    false),
                new FixOption(
                    "It does use the API",
                    "Keep the token, and pair it with RBAC narrow enough to justify it.",
                    "# Keep automount, and scope what the token can do:\napiVersion: rbac.authorization.k8s.io/v1\nkind: Role\nmetadata:\n  name: member-api\nrules:\n  - apiGroups: [\"\"]\n    resources: [configmaps]\n    resourceNames: [member-api-config]\n    verbs: [get, watch]",
                    "For operators, controllers and anything using leader election. The token stops being a "
                    + "liability once the permissions attached to it are small.",
                    false)
            ]));

        yield return new("CP-SEC-010", new(
            "An RBAC rule uses `*` for verbs, resources or apiGroups. A wildcard grants everything that exists "
            + "today and everything installed tomorrow — including CRDs from tools nobody has adopted yet.",
            [
                new FixOption(
                    "Name the verbs and resources",
                    "Grant exactly what the workload calls, and nothing else.",
                    "rules:\n  - apiGroups: [\"\"]\n    resources: [configmaps, secrets]\n    verbs: [get, list, watch]",
                    "Read the code, or watch the API server audit log, to see what it actually uses. Almost every "
                    + "wildcard turns out to be three verbs on two resources.",
                    IsRecommended: true),
                new FixOption(
                    "Scope it to named objects",
                    "Restrict the rule to the specific objects the workload owns.",
                    "rules:\n  - apiGroups: [\"\"]\n    resources: [configmaps]\n    resourceNames: [member-api-config, member-api-leader]\n    verbs: [get, update]",
                    "The tightest form, and ideal for leader election or a single config object. Note that "
                    + "resourceNames cannot restrict `list` or `watch` — those see the whole namespace.",
                    false),
                new FixOption(
                    "Narrow the scope instead of the verbs",
                    "If it genuinely needs broad verbs, at least confine them to one namespace.",
                    "kind: Role          # not ClusterRole\nmetadata:\n  namespace: member-platform",
                    "A Role is bounded by its namespace; a ClusterRole is not. Downgrading from cluster-wide to "
                    + "namespaced is often the single biggest reduction available.",
                    false)
            ]));

        yield return new("CP-SEC-011", new(
            "Nothing stops a process in this container from gaining more privileges than it started with — for "
            + "example through a setuid binary. Setting allowPrivilegeEscalation: false makes the kernel refuse, "
            + "which closes a common step in a local exploit chain.",
            [
                new FixOption(
                    "Refuse privilege escalation",
                    "One line, and almost never breaks anything.",
                    "securityContext:\n  allowPrivilegeEscalation: false",
                    "Safe for essentially every application container. It only affects processes that try to "
                    + "elevate, which a normal service never does.",
                    IsRecommended: true),
                new FixOption(
                    "Set it for the whole pod",
                    "Apply it alongside the other pod-level hardening so it is decided in one place.",
                    "spec:\n  template:\n    spec:\n      securityContext:\n        runAsNonRoot: true\n      containers:\n        - name: app\n          securityContext:\n            allowPrivilegeEscalation: false\n            readOnlyRootFilesystem: true\n            capabilities:\n              drop: [ALL]",
                    "allowPrivilegeEscalation is a container-level field, so it has to be repeated per container — "
                    + "but grouping the hardening keeps it reviewable.",
                    false)
            ]));

        yield return new("CP-SEC-012", new(
            "The container keeps the default set of Linux capabilities. Those are powers over the kernel — "
            + "changing file ownership, raw sockets, overriding permission checks — that a normal web service "
            + "never uses but an attacker inside the container certainly can.",
            [
                new FixOption(
                    "Drop everything",
                    "Remove all capabilities and add none back.",
                    "securityContext:\n  capabilities:\n    drop: [ALL]",
                    "Works for the overwhelming majority of application containers. If the process fails to start, "
                    + "the error names the capability it wanted — add just that one.",
                    IsRecommended: true),
                new FixOption(
                    "Drop everything, add one back",
                    "For the one common case: binding a port below 1024.",
                    "securityContext:\n  capabilities:\n    drop: [ALL]\n    add: [NET_BIND_SERVICE]",
                    "Only needed if the container itself listens on 80 or 443. Changing the app to listen on 8080 "
                    + "removes the need entirely.",
                    false)
            ]));

        yield return new("CP-SEC-013", new(
            "The pod shares a namespace with the node — the host network, PID or IPC namespace. That removes the "
            + "isolation between this pod and everything else on that machine: it can see the node's processes, "
            + "its network interfaces, or both.",
            [
                new FixOption(
                    "Use the pod's own namespaces",
                    "Remove the host namespace flags and let Kubernetes isolate the pod normally.",
                    "spec:\n  template:\n    spec:\n      hostNetwork: false\n      hostPID: false\n      hostIPC: false",
                    "The right answer for an application workload. If it was set to expose a port, a Service does "
                    + "that job without giving up isolation.",
                    IsRecommended: true),
                new FixOption(
                    "Expose the port properly instead",
                    "hostNetwork is often reached for when a NodePort or a Service was what was wanted.",
                    "apiVersion: v1\nkind: Service\nmetadata:\n  name: member-api\nspec:\n  type: NodePort\n  selector:\n    app.kubernetes.io/name: member-api\n  ports:\n    - port: 8080\n      nodePort: 30080",
                    "Gives you node-level reachability without the pod seeing the node's network stack. Still "
                    + "prefer an Ingress or Gateway where one exists.",
                    false),
                new FixOption(
                    "It is a node-level agent",
                    "Monitoring agents and CNI plugins do need the host namespace.",
                    "# .chartpilot.yaml\nsuppress:\n  - id: CP-SEC-013\n    resource: DaemonSet/node-exporter\n    reason: \"Node metrics agent; hostNetwork and hostPID are required to read node state.\"\n    expires: 2027-01-01",
                    "Genuine for infrastructure DaemonSets. If it is a Deployment serving user traffic, this is "
                    + "the wrong option.",
                    false)
            ]));

        yield return new("CP-SEC-014", new(
            "The chart renders a route that is reachable from outside the cluster, and the profile or the data "
            + "classification you selected says this service should not be. This is not about how the route is "
            + "configured — it is about whether it should exist at all.",
            [
                new FixOption(
                    "Make the route internal",
                    "Bind it to an internal gateway instead of the public one.",
                    "istio:\n  gateway:\n    selector:\n      istio: internal-ingressgateway\n    hosts:\n      - member-api.internal.example.com",
                    "The usual fix for a service that was only ever meant to serve other services. Callers outside "
                    + "the network then reach it through whatever fronts the platform, not directly.",
                    IsRecommended: true),
                new FixOption(
                    "Keep it public, and guard it",
                    "If it must be public, put an explicit authorization decision in front of it.",
                    "apiVersion: security.istio.io/v1\nkind: AuthorizationPolicy\nmetadata:\n  name: member-api\nspec:\n  selector:\n    matchLabels:\n      app.kubernetes.io/name: member-api\n  action: ALLOW\n  rules:\n    - from:\n        - source:\n            requestPrincipals: [\"https://accounts.example.com/*\"]",
                    "Public plus authenticated is a defensible design. Public plus a profile that forbids it is a "
                    + "disagreement someone needs to settle — often by changing the profile, if the profile is wrong.",
                    false),
                new FixOption(
                    "Change the profile",
                    "If the service is genuinely a public web service, say so.",
                    "# review it under the profile that matches what it is:\n#   chartpilot check ./chart --profile public-web-service",
                    "Worth checking before you change the chart. A finding caused by reviewing an internet-facing "
                    + "service under an internal-only profile is a mislabelled review, not a broken chart.",
                    false)
            ]));
    }
}
