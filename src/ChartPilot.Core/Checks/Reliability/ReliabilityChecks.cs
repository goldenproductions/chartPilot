using ChartPilot.Core.Manifests;
using YamlDotNet.RepresentationModel;

namespace ChartPilot.Core.Checks;

// The CP-REL-* family: does this workload survive a rollout, a node drain and a bad build?
// Every rule here emits Descriptor.DefaultSeverity; the engine resolves the real severity.

/// <summary>CP-REL-001 — a container with no readinessProbe takes traffic before it can serve it.</summary>
internal sealed class ReadinessProbeCheck : LongLivedWorkloadCheckBase
{
    public override CheckDescriptor Descriptor { get; } = new(
        "CP-REL-001",
        "Container has no readinessProbe",
        CheckCategory.Reliability,
        Severity.Warning,
        "Without a readinessProbe the Service adds the pod to its endpoints as soon as the process starts, so the "
        + "first requests after every rollout hit a container that is not ready yet.",
        "readinessProbe:\n  httpGet:\n    path: /healthz/ready\n    port: http\n  initialDelaySeconds: 5\n  periodSeconds: 10",
        "https://kubernetes.io/docs/tasks/configure-pod-container/configure-liveness-readiness-startup-probes/");

    public override IEnumerable<Finding> Evaluate(CheckContext context)
    {
        foreach (var workload in CheckHelpers.LongLivedWorkloads(context))
        {
            foreach (var container in CheckHelpers.AppContainers(workload))
            {
                if (ManifestNavigator.Get(container.Node, "readinessProbe") is null)
                {
                    yield return Violation(
                        workload,
                        $"Container '{container.Name}' has no readinessProbe.",
                        container.YamlPath);
                }
            }
        }
    }
}

/// <summary>CP-REL-002 — a container with no livenessProbe can wedge without ever being restarted.</summary>
internal sealed class LivenessProbeCheck : LongLivedWorkloadCheckBase
{
    public override CheckDescriptor Descriptor { get; } = new(
        "CP-REL-002",
        "Container has no livenessProbe",
        CheckCategory.Reliability,
        Severity.Warning,
        "A deadlocked or wedged process keeps its pod Running forever. A livenessProbe is what turns that silent "
        + "outage into an automatic restart, and it is the difference between a self-healing service and a pager.",
        "livenessProbe:\n  httpGet:\n    path: /healthz/live\n    port: http\n  initialDelaySeconds: 15\n  periodSeconds: 20",
        "https://kubernetes.io/docs/tasks/configure-pod-container/configure-liveness-readiness-startup-probes/");

    public override IEnumerable<Finding> Evaluate(CheckContext context)
    {
        foreach (var workload in CheckHelpers.LongLivedWorkloads(context))
        {
            foreach (var container in CheckHelpers.AppContainers(workload))
            {
                if (ManifestNavigator.Get(container.Node, "livenessProbe") is null)
                {
                    yield return Violation(
                        workload,
                        $"Container '{container.Name}' has no livenessProbe.",
                        container.YamlPath);
                }
            }
        }
    }
}

/// <summary>CP-REL-003 — no resource requests means the scheduler is guessing.</summary>
internal sealed class ResourceRequestsCheck : WorkloadCheckBase
{
    public override CheckDescriptor Descriptor { get; } = new(
        "CP-REL-003",
        "Container has no resource requests",
        CheckCategory.Reliability,
        Severity.Warning,
        "Requests are what the scheduler packs against and what the QoS class is derived from. A container without "
        + "them lands in BestEffort and is the first thing evicted when a node comes under pressure.",
        "resources:\n  requests:\n    cpu: 100m\n    memory: 128Mi",
        "https://kubernetes.io/docs/concepts/configuration/manage-resources-containers/");

    public override IEnumerable<Finding> Evaluate(CheckContext context)
    {
        foreach (var workload in CheckHelpers.Workloads(context))
        {
            foreach (var container in ManifestNavigator.GetContainers(workload))
            {
                var cpu = ManifestNavigator.GetString(container.Node, "resources.requests.cpu");
                var memory = ManifestNavigator.GetString(container.Node, "resources.requests.memory");

                if (cpu is null && memory is null)
                {
                    yield return Violation(
                        workload,
                        $"Container '{container.Name}' declares no resources.requests.",
                        container.YamlPath + ".resources");
                }
            }
        }
    }
}

/// <summary>CP-REL-004 — no resource limits means one container can starve the node.</summary>
internal sealed class ResourceLimitsCheck : WorkloadCheckBase
{
    public override CheckDescriptor Descriptor { get; } = new(
        "CP-REL-004",
        "Container has no resource limits",
        CheckCategory.Reliability,
        Severity.Warning,
        "A container with no memory limit can consume the whole node and take unrelated workloads down with it. "
        + "A container with no CPU limit can starve every other process on the node of scheduler time. Limits turn "
        + "a single misbehaving process into a single restarting or throttled pod.",
        "resources:\n  limits:\n    cpu: 500m\n    memory: 512Mi",
        "https://kubernetes.io/docs/concepts/configuration/manage-resources-containers/");

    public override IEnumerable<Finding> Evaluate(CheckContext context)
    {
        foreach (var workload in CheckHelpers.Workloads(context))
        {
            foreach (var container in ManifestNavigator.GetContainers(workload))
            {
                // The profile flag is RequireResourceLimits, plural: both limits are part of the
                // requirement, and one of the two being present is not the same as having limits.
                var missing = new List<string>(2);

                if (ManifestNavigator.GetString(container.Node, "resources.limits.memory") is null)
                {
                    missing.Add("resources.limits.memory");
                }

                if (ManifestNavigator.GetString(container.Node, "resources.limits.cpu") is null)
                {
                    missing.Add("resources.limits.cpu");
                }

                if (missing.Count > 0)
                {
                    yield return Violation(
                        workload,
                        $"Container '{container.Name}' declares no {string.Join(" and no ", missing)}.",
                        container.YamlPath + ".resources");
                }
            }
        }
    }
}

/// <summary>CP-REL-005 — fewer replicas than the floor for this profile and environment.</summary>
internal sealed class MinimumReplicasCheck : CheckBase, IConditionalCheck
{
    /// <summary>The floor a production environment carries even when the profile does not state one.</summary>
    private const int ProductionFloor = 2;

    public override CheckDescriptor Descriptor { get; } = new(
        "CP-REL-005",
        "Replica count is below the required minimum",
        CheckCategory.Reliability,
        Severity.Warning,
        "A single replica means every rollout, node drain and spot reclaim is a full outage. The golden path "
        + "profile states the floor the platform team is willing to support, and production carries that floor "
        + "whether or not the selected profile spells it out.",
        "spec:\n  replicas: 2   # or add a HorizontalPodAutoscaler with minReplicas >= 2",
        "https://kubernetes.io/docs/concepts/workloads/controllers/deployment/");

    /// <summary>
    /// The floor for this review: the profile's minimum, raised to two in a production environment.
    /// Reviewing values-prod.yaml under a permissive profile still reports a single replica — which
    /// is the spec's "only one replica in prod" item, and the reason the rule's own Warning severity
    /// is reachable (a profile-mandated minimum is promoted to Critical by the severity resolver).
    /// </summary>
    private static int Minimum(CheckContext context)
    {
        var profileMinimum = context.Profile.Requirements.MinReplicas;
        return context.IsProductionEnvironment ? Math.Max(profileMinimum, ProductionFloor) : profileMinimum;
    }

    public bool IsApplicable(CheckContext context)
        => Minimum(context) > 1 && context.Graph.ByKinds("Deployment", "StatefulSet").Any();

    public override IEnumerable<Finding> Evaluate(CheckContext context)
    {
        var minimum = Minimum(context);
        if (minimum <= 1)
        {
            yield break;
        }

        var because = context.Profile.Requirements.MinReplicas >= minimum
            ? $"profile '{context.Profile.Id}'"
            : $"environment '{context.Environment}'";

        foreach (var workload in context.Graph.ByKinds("Deployment", "StatefulSet"))
        {
            if (HasAutoscalerAbove(context, workload, minimum))
            {
                continue;
            }

            var replicas = ManifestNavigator.GetInt(workload.Root, "spec.replicas") ?? 1;

            if (replicas < minimum)
            {
                yield return Violation(
                    workload,
                    $"spec.replicas is {replicas}, but {because} requires at least {minimum}.",
                    "spec.replicas");
            }
        }
    }

    private static bool HasAutoscalerAbove(CheckContext context, RenderedResource workload, int minimum)
    {
        foreach (var hpa in context.Graph.ByKind("HorizontalPodAutoscaler"))
        {
            if (!TargetsWorkload(hpa, workload))
            {
                continue;
            }

            if ((ManifestNavigator.GetInt(hpa.Root, "spec.minReplicas") ?? 1) >= minimum)
            {
                return true;
            }
        }

        return false;
    }

    internal static bool TargetsWorkload(RenderedResource autoscaler, RenderedResource workload)
        => string.Equals(ManifestNavigator.GetString(autoscaler.Root, "spec.scaleTargetRef.kind"), workload.Kind, StringComparison.Ordinal)
           && string.Equals(ManifestNavigator.GetString(autoscaler.Root, "spec.scaleTargetRef.name"), workload.Name, StringComparison.Ordinal);
}

/// <summary>CP-REL-006 — no PodDisruptionBudget covers the workload, so a node drain can take it all out.</summary>
internal sealed class PodDisruptionBudgetCheck : LongLivedWorkloadCheckBase
{
    public override CheckDescriptor Descriptor { get; } = new(
        "CP-REL-006",
        "No PodDisruptionBudget covers the workload",
        CheckCategory.Reliability,
        Severity.Warning,
        "Cluster upgrades and node autoscaling drain nodes. Without a PDB the eviction API is free to remove every "
        + "replica of a service at once, which turns routine platform maintenance into a customer-visible outage.",
        "apiVersion: policy/v1\nkind: PodDisruptionBudget\nmetadata:\n  name: my-app\nspec:\n  minAvailable: 1\n"
        + "  selector:\n    matchLabels:\n      app.kubernetes.io/name: my-app",
        "https://kubernetes.io/docs/tasks/run-application/configure-pdb/");

    public override bool IsApplicable(CheckContext context)
        => context.Graph.ByKinds("Deployment", "StatefulSet").Any();

    public override IEnumerable<Finding> Evaluate(CheckContext context)
    {
        foreach (var workload in context.Graph.ByKinds("Deployment", "StatefulSet"))
        {
            // "targets-workload" is the PodDisruptionBudget -> workload edge resolved from its selector.
            if (!CheckHelpers.IsReachedBy(context, workload, GraphRelations.TargetsWorkload, "PodDisruptionBudget"))
            {
                yield return Violation(
                    workload,
                    $"No PodDisruptionBudget selects the pods of {workload.Kind}/{workload.Name}.",
                    "spec.template.metadata.labels");
            }
        }
    }
}

/// <summary>CP-REL-007 — a Deployment that replaces rather than rolls.</summary>
internal sealed class RollingUpdateStrategyCheck : CheckBase, IConditionalCheck
{
    public override CheckDescriptor Descriptor { get; } = new(
        "CP-REL-007",
        "Deployment has no rolling update strategy",
        CheckCategory.Reliability,
        Severity.Warning,
        "strategy.type: Recreate tears every pod down before starting the new ones, so every deploy is a planned "
        + "outage. Stating the rolling strategy explicitly also documents the surge the cluster must absorb.",
        "spec:\n  strategy:\n    type: RollingUpdate\n    rollingUpdate:\n      maxUnavailable: 0\n      maxSurge: 1",
        "https://kubernetes.io/docs/concepts/workloads/controllers/deployment/#strategy");

    public bool IsApplicable(CheckContext context) => context.Graph.ContainsKind("Deployment");

    public override IEnumerable<Finding> Evaluate(CheckContext context)
    {
        foreach (var deployment in context.Graph.ByKind("Deployment"))
        {
            var type = ManifestNavigator.GetString(deployment.Root, "spec.strategy.type");

            if (string.Equals(type, "Recreate", StringComparison.Ordinal))
            {
                yield return Violation(
                    deployment,
                    "spec.strategy.type is Recreate, so every deploy is a full outage of this workload.",
                    "spec.strategy.type");
            }
            else if (type is null)
            {
                yield return Violation(
                    deployment,
                    "No spec.strategy is declared, so the rollout surge and unavailability are left implicit.",
                    "spec.strategy");
            }
        }
    }
}

/// <summary>CP-REL-008 — slow-starting workloads need a startupProbe so the livenessProbe does not kill them.</summary>
internal sealed class StartupProbeCheck : LongLivedWorkloadCheckBase
{
    public override CheckDescriptor Descriptor { get; } = new(
        "CP-REL-008",
        "Slow-starting workload has no startupProbe",
        CheckCategory.Reliability,
        Severity.Info,
        "A workload that runs init containers — migrations, cache warm-up, config fetch — starts slowly. Without a "
        + "startupProbe the livenessProbe begins counting immediately and restart-loops the pod before it is up.",
        "startupProbe:\n  httpGet:\n    path: /healthz/started\n    port: http\n  failureThreshold: 30\n  periodSeconds: 10",
        "https://kubernetes.io/docs/tasks/configure-pod-container/configure-liveness-readiness-startup-probes/");

    public override IEnumerable<Finding> Evaluate(CheckContext context)
    {
        foreach (var workload in CheckHelpers.LongLivedWorkloads(context))
        {
            if (!CheckHelpers.InitContainers(workload).Any())
            {
                continue;
            }

            foreach (var container in CheckHelpers.AppContainers(workload))
            {
                if (ManifestNavigator.Get(container.Node, "livenessProbe") is not null
                    && ManifestNavigator.Get(container.Node, "startupProbe") is null)
                {
                    yield return Violation(
                        workload,
                        $"Container '{container.Name}' runs after an init container and has a livenessProbe but no startupProbe.",
                        container.YamlPath);
                }
            }
        }
    }
}

/// <summary>CP-REL-009 — a CronJob with no concurrency or history bounds.</summary>
internal sealed class CronJobHygieneCheck : CheckBase, IConditionalCheck
{
    public override CheckDescriptor Descriptor { get; } = new(
        "CP-REL-009",
        "CronJob has no concurrencyPolicy or failed-job history limit",
        CheckCategory.Reliability,
        Severity.Warning,
        "The default concurrencyPolicy is Allow, so a job that runs longer than its schedule silently piles up "
        + "overlapping runs. Unbounded failed-job history then keeps every one of those pods around forever.",
        "spec:\n  concurrencyPolicy: Forbid\n  failedJobsHistoryLimit: 3\n  successfulJobsHistoryLimit: 1",
        "https://kubernetes.io/docs/concepts/workloads/controllers/cron-jobs/");

    public bool IsApplicable(CheckContext context) => context.Graph.ContainsKind("CronJob");

    public override IEnumerable<Finding> Evaluate(CheckContext context)
    {
        foreach (var cronJob in context.Graph.ByKind("CronJob"))
        {
            if (ManifestNavigator.GetString(cronJob.Root, "spec.concurrencyPolicy") is null)
            {
                yield return Violation(
                    cronJob,
                    "spec.concurrencyPolicy is not set, so overlapping runs are allowed by default.",
                    "spec.concurrencyPolicy");
            }

            if (ManifestNavigator.GetInt(cronJob.Root, "spec.failedJobsHistoryLimit") is null)
            {
                yield return Violation(
                    cronJob,
                    "spec.failedJobsHistoryLimit is not set, so failed job pods accumulate in the namespace.",
                    "spec.failedJobsHistoryLimit");
            }
        }
    }
}

/// <summary>CP-REL-010 — a single-replica Deployment with nothing to scale it.</summary>
internal sealed class AutoscalingCheck : CheckBase, IConditionalCheck
{
    public override CheckDescriptor Descriptor { get; } = new(
        "CP-REL-010",
        "Single-replica Deployment has no HorizontalPodAutoscaler",
        CheckCategory.Reliability,
        Severity.Info,
        "One replica and no autoscaler means the service has neither redundancy nor headroom: it cannot survive a "
        + "node drain and it cannot absorb a traffic spike without a manual edit.",
        "apiVersion: autoscaling/v2\nkind: HorizontalPodAutoscaler\nmetadata:\n  name: my-app\nspec:\n"
        + "  scaleTargetRef:\n    apiVersion: apps/v1\n    kind: Deployment\n    name: my-app\n"
        + "  minReplicas: 2\n  maxReplicas: 6",
        "https://kubernetes.io/docs/tasks/run-application/horizontal-pod-autoscale/");

    public bool IsApplicable(CheckContext context) => context.Graph.ContainsKind("Deployment");

    public override IEnumerable<Finding> Evaluate(CheckContext context)
    {
        foreach (var deployment in context.Graph.ByKind("Deployment"))
        {
            var replicas = ManifestNavigator.GetInt(deployment.Root, "spec.replicas") ?? 1;
            if (replicas > 1)
            {
                continue;
            }

            var hasAutoscaler = context.Graph
                .ByKind("HorizontalPodAutoscaler")
                .Any(hpa => MinimumReplicasCheck.TargetsWorkload(hpa, deployment));

            if (!hasAutoscaler)
            {
                yield return Violation(
                    deployment,
                    $"Deployment/{deployment.Name} runs {replicas} replica and has no HorizontalPodAutoscaler.",
                    "spec.replicas");
            }
        }
    }
}
