namespace ChartPilot.Core.Checks.Guidance;

/// <summary>Authored guidance for the CP-REL-* family.</summary>
internal static class ReliabilityGuidance
{
    public static IEnumerable<KeyValuePair<string, CheckGuidance>> Entries()
    {
        yield return new("CP-REL-001", new(
            "Kubernetes has no way to tell whether this container is ready to serve. It adds the pod to the "
            + "Service the moment the process starts, so during every rollout the first requests land on a "
            + "container that is still warming up.",
            [
                new FixOption(
                    "Probe a readiness endpoint",
                    "Point the probe at an endpoint that returns 200 only when the app can actually serve.",
                    "readinessProbe:\n  httpGet:\n    path: /healthz/ready\n    port: http\n  initialDelaySeconds: 5\n  periodSeconds: 10\n  failureThreshold: 3",
                    "The right answer. The endpoint should check what the request path needs — a database "
                    + "connection, a loaded cache — and not merely that the process is alive.",
                    IsRecommended: true),
                new FixOption(
                    "Probe the port",
                    "If there is no health endpoint, at least wait until the socket accepts connections.",
                    "readinessProbe:\n  tcpSocket:\n    port: http\n  initialDelaySeconds: 5\n  periodSeconds: 10",
                    "Better than nothing and needs no application change. It cannot tell you the app is healthy, "
                    + "only that something is listening.",
                    false),
                new FixOption(
                    "Run a command",
                    "For workloads with no network listener at all.",
                    "readinessProbe:\n  exec:\n    command: [/bin/sh, -c, \"test -f /tmp/ready\"]\n  periodSeconds: 10",
                    "Useful for queue consumers and batch-style services. Costs a process spawn on every probe, so "
                    + "keep the command cheap.",
                    false)
            ]));

        yield return new("CP-REL-002", new(
            "Nothing detects a wedged process. If the application deadlocks, stops its event loop or hangs on a "
            + "lock, the pod stays Running and keeps its place in the Service forever — a silent outage that only "
            + "a human notices.",
            [
                new FixOption(
                    "Probe a liveness endpoint",
                    "Restart the container when it stops being able to answer at all.",
                    "livenessProbe:\n  httpGet:\n    path: /healthz/live\n    port: http\n  initialDelaySeconds: 15\n  periodSeconds: 20\n  failureThreshold: 3",
                    "Keep this endpoint dumber than the readiness one: it should not check dependencies. A "
                    + "liveness probe that fails when the database is down restarts every pod during a database "
                    + "incident, turning an outage into a worse one.",
                    IsRecommended: true),
                new FixOption(
                    "Probe the port",
                    "Restart when the process stops accepting connections.",
                    "livenessProbe:\n  tcpSocket:\n    port: http\n  initialDelaySeconds: 15\n  periodSeconds: 20",
                    "Catches a crashed listener but not a deadlocked request handler, which is the more common "
                    + "failure. Use when adding an endpoint is not an option.",
                    false)
            ]));

        yield return new("CP-REL-003", new(
            "The container asks for no CPU or memory, so the scheduler treats it as needing nothing. It lands in "
            + "the BestEffort quality-of-service class, which is the first thing the kubelet evicts when a node "
            + "runs short of memory.",
            [
                new FixOption(
                    "Set requests from observed usage",
                    "Ask for roughly what the container uses at rest.",
                    "resources:\n  requests:\n    cpu: 100m\n    memory: 128Mi",
                    "Take the numbers from a running instance rather than guessing — request too much and you "
                    + "waste cluster capacity, too little and the pod is evicted under pressure.",
                    IsRecommended: true),
                new FixOption(
                    "Requests equal to limits",
                    "Ask for exactly what you cap at, which gives the pod the Guaranteed QoS class.",
                    "resources:\n  requests:\n    cpu: 500m\n    memory: 512Mi\n  limits:\n    cpu: 500m\n    memory: 512Mi",
                    "The strongest scheduling guarantee: last to be evicted, never throttled below its request. "
                    + "It also reserves the capacity whether or not the pod uses it, so it costs more.",
                    false)
            ]));

        yield return new("CP-REL-004", new(
            "The container has no ceiling. Without a memory limit it can consume the whole node and take "
            + "unrelated workloads down with it; without a CPU limit it can starve every other process on the "
            + "node of scheduler time.",
            [
                new FixOption(
                    "Limit memory, request CPU",
                    "Cap the resource that kills nodes, and let CPU burst.",
                    "resources:\n  requests:\n    cpu: 100m\n    memory: 128Mi\n  limits:\n    memory: 512Mi",
                    "Increasingly the recommended shape: a memory limit contains the blast radius of a leak, while "
                    + "omitting the CPU limit avoids throttling a latency-sensitive service that could otherwise "
                    + "use idle capacity.",
                    IsRecommended: true),
                new FixOption(
                    "Limit both",
                    "Cap CPU as well, for predictable behaviour on a shared node.",
                    "resources:\n  requests:\n    cpu: 100m\n    memory: 128Mi\n  limits:\n    cpu: 500m\n    memory: 512Mi",
                    "Predictable and easy to reason about, and often required by a platform policy. Be aware that "
                    + "CPU limits throttle in short bursts, which shows up as p99 latency rather than as an error.",
                    false)
            ]));

        yield return new("CP-REL-005", new(
            "The workload runs fewer replicas than the profile requires. With a single replica every node drain, "
            + "rollout and spot reclamation is a full outage of this service — there is nothing else serving while "
            + "the pod moves.",
            [
                new FixOption(
                    "Run at least two",
                    "Two replicas across two nodes survive one node going away.",
                    "replicaCount: 2\n\n# and spread them:\naffinity:\n  podAntiAffinity:\n    preferredDuringSchedulingIgnoredDuringExecution:\n      - weight: 100\n        podAffinityTerm:\n          topologyKey: kubernetes.io/hostname\n          labelSelector:\n            matchLabels:\n              app.kubernetes.io/name: member-api",
                    "Two replicas without anti-affinity can both land on the same node, which defeats the point. "
                    + "The soft rule above still schedules if the cluster is small.",
                    IsRecommended: true),
                new FixOption(
                    "Autoscale from a floor of two",
                    "Set the minimum and let load decide the rest.",
                    "autoscaling:\n  enabled: true\n  minReplicas: 2\n  maxReplicas: 6\n  targetCPUUtilizationPercentage: 70",
                    "Handles both availability and traffic spikes. Remember to stop setting replicaCount once an "
                    + "HPA owns the count, or the two fight each other on every deploy.",
                    false),
                new FixOption(
                    "It is a singleton by design",
                    "Some workloads genuinely must not run twice.",
                    "# .chartpilot.yaml\nsuppress:\n  - id: CP-REL-005\n    reason: \"Single-writer migration runner; concurrent instances would corrupt state.\"\n    expires: 2027-01-01",
                    "Legitimate for leader-elected controllers and migration jobs. If it is a stateless HTTP "
                    + "service, it is not a singleton — it is a single point of failure.",
                    false)
            ]));

        yield return new("CP-REL-006", new(
            "Nothing tells Kubernetes how much of this service must stay up during voluntary disruption. When a "
            + "node is drained for an upgrade or scaled down by the autoscaler, the eviction API is free to remove "
            + "every replica at once.",
            [
                new FixOption(
                    "Guarantee a minimum",
                    "State how many replicas must remain available during a drain.",
                    "apiVersion: policy/v1\nkind: PodDisruptionBudget\nmetadata:\n  name: member-api\nspec:\n  minAvailable: 1\n  selector:\n    matchLabels:\n      app.kubernetes.io/name: member-api",
                    "Simple and effective. With only one replica, minAvailable: 1 blocks node drains entirely — so "
                    + "pair this with at least two replicas or the cluster cannot be maintained.",
                    IsRecommended: true),
                new FixOption(
                    "Allow a proportion to go",
                    "Express the budget as a fraction, so it scales with the replica count.",
                    "spec:\n  maxUnavailable: 25%\n  selector:\n    matchLabels:\n      app.kubernetes.io/name: member-api",
                    "Better for services that autoscale, since the budget stays sensible at 2 replicas and at 20. "
                    + "Rounds in the cluster's favour, so check the arithmetic at your minimum size.",
                    false)
            ]));

        yield return new("CP-REL-007", new(
            "The Deployment uses the Recreate strategy, which terminates every existing pod before starting any "
            + "new ones. Every deployment of this service is therefore a planned, customer-visible outage lasting "
            + "as long as the new pods take to become ready.",
            [
                new FixOption(
                    "Roll without going below capacity",
                    "Add new pods before removing old ones.",
                    "spec:\n  strategy:\n    type: RollingUpdate\n    rollingUpdate:\n      maxUnavailable: 0\n      maxSurge: 1",
                    "Zero-downtime deploys, at the cost of briefly running one extra pod's worth of capacity. "
                    + "Requires the old and new versions to tolerate running side by side.",
                    IsRecommended: true),
                new FixOption(
                    "Roll faster, allow a dip",
                    "Replace pods in larger steps when a brief capacity drop is acceptable.",
                    "spec:\n  strategy:\n    type: RollingUpdate\n    rollingUpdate:\n      maxUnavailable: 25%\n      maxSurge: 25%",
                    "Quicker rollouts and no extra capacity needed. Only for services with enough headroom that "
                    + "losing a quarter of them for a moment does not matter.",
                    false),
                new FixOption(
                    "Recreate is required",
                    "Some workloads cannot have two versions running at once.",
                    "# .chartpilot.yaml\nsuppress:\n  - id: CP-REL-007\n    reason: \"Holds an exclusive lock on a ReadWriteOnce volume; two versions cannot coexist.\"\n    expires: 2027-01-01",
                    "True for single-writer workloads and some schema migrations. Say so explicitly rather than "
                    + "leaving the next reader to wonder whether it was deliberate.",
                    false)
            ]));

        yield return new("CP-REL-008", new(
            "This workload does slow start-up work — init containers, migrations, a cache warm-up — but has no "
            + "startupProbe. The livenessProbe therefore starts counting immediately, and can restart the pod "
            + "before it has finished coming up, forever.",
            [
                new FixOption(
                    "Add a startup probe",
                    "Give the container a generous window to come up, then hand over to liveness.",
                    "startupProbe:\n  httpGet:\n    path: /healthz/live\n    port: http\n  failureThreshold: 30\n  periodSeconds: 10   # allows up to 5 minutes to start",
                    "The correct mechanism: liveness is suspended until the startup probe first succeeds, so you "
                    + "can keep liveness aggressive without punishing slow starts.",
                    IsRecommended: true),
                new FixOption(
                    "Delay the liveness probe instead",
                    "Push the liveness probe's first check out past the worst-case start.",
                    "livenessProbe:\n  initialDelaySeconds: 120",
                    "Works, and needs no extra probe. The cost is that a container which wedges after starting "
                    + "goes unnoticed for the whole delay — a startupProbe does not have that gap.",
                    false)
            ]));

        yield return new("CP-REL-009", new(
            "The CronJob does not say what to do when a run overruns its schedule, or how much failed history to "
            + "keep. The default is to allow overlapping runs, so a job that gets slow quietly ends up with "
            + "several copies of itself running at once.",
            [
                new FixOption(
                    "Never overlap, keep a little history",
                    "Skip the new run if the previous one is still going, and bound what is retained.",
                    "spec:\n  concurrencyPolicy: Forbid\n  successfulJobsHistoryLimit: 3\n  failedJobsHistoryLimit: 3\n  startingDeadlineSeconds: 300",
                    "The right default for almost any job that touches shared state. Forbid means a slow run "
                    + "silently skips the next slot, so alert on the job's own success rather than assuming it ran.",
                    IsRecommended: true),
                new FixOption(
                    "Replace the running job",
                    "Kill the in-flight run and start the new one.",
                    "spec:\n  concurrencyPolicy: Replace\n  failedJobsHistoryLimit: 3",
                    "For jobs where the freshest run matters and a partial run is harmless — a cache refresh, say. "
                    + "Wrong for anything that writes.",
                    false),
                new FixOption(
                    "Overlap is fine, bound the history",
                    "Keep concurrency, but stop the failed pods accumulating.",
                    "spec:\n  concurrencyPolicy: Allow\n  successfulJobsHistoryLimit: 1\n  failedJobsHistoryLimit: 3",
                    "Only when runs are genuinely independent and idempotent. The history limits matter either "
                    + "way — unbounded failed jobs keep their pods, and their logs, forever.",
                    false)
            ]));

        yield return new("CP-REL-010", new(
            "One replica and no autoscaler: the service has neither redundancy nor headroom. It cannot survive a "
            + "node drain, and it cannot absorb a traffic spike without somebody editing values and deploying.",
            [
                new FixOption(
                    "Add an autoscaler with a floor of two",
                    "Fix redundancy and elasticity in one change.",
                    "autoscaling:\n  enabled: true\n  minReplicas: 2\n  maxReplicas: 6\n  targetCPUUtilizationPercentage: 70",
                    "The usual answer for a request-serving service. Needs resource requests to be set, because "
                    + "the HPA scales on utilisation relative to the request.",
                    IsRecommended: true),
                new FixOption(
                    "Just run two",
                    "Take the redundancy without introducing autoscaling.",
                    "replicaCount: 2",
                    "Simpler, and enough for a service with steady traffic. You are choosing to handle spikes by "
                    + "over-provisioning rather than by scaling.",
                    false)
            ]));
    }
}
