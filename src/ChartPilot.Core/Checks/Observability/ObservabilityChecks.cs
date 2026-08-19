using ChartPilot.Core.Manifests;

namespace ChartPilot.Core.Checks;

// The CP-OBS-* family: can the platform team see this service once it is running?
// Every rule emits Descriptor.DefaultSeverity; the engine resolves the real severity.

/// <summary>CP-OBS-001 — nothing tells Prometheus to scrape this service.</summary>
internal sealed class MetricsScrapeCheck : WorkloadCheckBase
{
    public override CheckDescriptor Descriptor { get; } = new(
        "CP-OBS-001",
        "No ServiceMonitor and no Prometheus scrape annotation",
        CheckCategory.Operability,
        Severity.Warning,
        "A service nobody scrapes has no SLO, no alerting and no capacity data. Discovery has to be declared in the "
        + "chart, because the platform team cannot retrofit it for every service by hand.",
        "apiVersion: monitoring.coreos.com/v1\nkind: ServiceMonitor\nmetadata:\n  name: my-app\nspec:\n"
        + "  selector:\n    matchLabels:\n      app.kubernetes.io/name: my-app\n"
        + "  endpoints:\n    - port: metrics\n      path: /metrics\n      interval: 30s",
        "https://prometheus-operator.dev/docs/api-reference/api/#monitoring.coreos.com/v1.ServiceMonitor");

    public override IEnumerable<Finding> Evaluate(CheckContext context)
    {
        if (context.Graph.ContainsKind("ServiceMonitor") || context.Graph.ContainsKind("PodMonitor"))
        {
            yield break;
        }

        if (HasScrapeAnnotation(context))
        {
            yield break;
        }

        yield return ChartViolation(
            "The chart renders no ServiceMonitor or PodMonitor, and no resource carries prometheus.io/scrape.");
    }

    private static bool HasScrapeAnnotation(CheckContext context)
    {
        foreach (var resource in context.Graph.Resources)
        {
            if (ManifestNavigator.GetAnnotations(resource).ContainsKey("prometheus.io/scrape"))
            {
                return true;
            }

            if (!CheckHelpers.IsPodCarrying(resource))
            {
                continue;
            }

            var templatePath = resource.Kind switch
            {
                "Pod" => "metadata.annotations",
                "CronJob" => "spec.jobTemplate.spec.template.metadata.annotations",
                _ => "spec.template.metadata.annotations"
            };

            if (ManifestNavigator.GetStringMap(resource.Root, templatePath).ContainsKey("prometheus.io/scrape"))
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>CP-OBS-002 — the recommended Kubernetes labels are missing.</summary>
internal sealed class StandardLabelsCheck : CheckBase, IConditionalCheck
{
    public override CheckDescriptor Descriptor { get; } = new(
        "CP-OBS-002",
        "Resource is missing the recommended Kubernetes labels",
        CheckCategory.Operability,
        Severity.Warning,
        "The app.kubernetes.io labels are the join key every dashboard, alert route, cost report and incident query "
        + "in the cluster relies on. A resource without them is invisible to all of that tooling.",
        "metadata:\n  labels:\n    app.kubernetes.io/name: my-app\n    app.kubernetes.io/instance: my-app\n"
        + "    app.kubernetes.io/version: \"1.4.2\"\n    app.kubernetes.io/managed-by: Helm\n"
        + "    app.kubernetes.io/part-of: my-platform",
        "https://kubernetes.io/docs/concepts/overview/working-with-objects/common-labels/");

    /// <summary>Only the resources a platform team actually indexes are checked, to keep the noise proportional.</summary>
    internal static bool IsLabelled(RenderedResource resource)
        => CheckHelpers.IsPodCarrying(resource) || resource.Kind == "Service";

    public bool IsApplicable(CheckContext context) => context.Graph.Resources.Any(IsLabelled);

    public override IEnumerable<Finding> Evaluate(CheckContext context)
    {
        foreach (var resource in context.Graph.Resources.Where(IsLabelled))
        {
            var labels = ManifestNavigator.GetLabels(resource);

            var missing = CheckHelpers.StandardLabels
                .Where(label => !labels.ContainsKey(label))
                .ToArray();

            if (missing.Length > 0)
            {
                yield return Violation(
                    resource,
                    $"{resource.Kind}/{resource.Name} is missing {string.Join(", ", missing)}.",
                    "metadata.labels");
            }
        }
    }
}

/// <summary>CP-OBS-003 — container ports are unnamed, so nothing can bind to them by name.</summary>
internal sealed class NamedContainerPortCheck : LongLivedWorkloadCheckBase
{
    private static readonly string[] ExpectedNames = ["http", "metrics"];

    public override CheckDescriptor Descriptor { get; } = new(
        "CP-OBS-003",
        "Workload exposes no port named http or metrics",
        CheckCategory.Operability,
        Severity.Info,
        "Named ports are what let a Service, a probe and a ServiceMonitor refer to the same port without hard-coding "
        + "a number in four places — so changing the listen port becomes a one-line change instead of a hunt.",
        "ports:\n  - name: http\n    containerPort: 8080\n    protocol: TCP\n"
        + "  - name: metrics\n    containerPort: 9090\n    protocol: TCP",
        "https://kubernetes.io/docs/concepts/services-networking/service/#defining-a-service");

    public override IEnumerable<Finding> Evaluate(CheckContext context)
    {
        foreach (var workload in CheckHelpers.LongLivedWorkloads(context))
        {
            var containers = CheckHelpers.AppContainers(workload).ToList();
            if (containers.Count == 0)
            {
                continue;
            }

            var named = containers
                .SelectMany(container => ManifestNavigator.GetSequence(container.Node, "ports"))
                .Select(port => ManifestNavigator.GetString(port, "name"))
                .Where(name => name is not null)
                .Any(name => ExpectedNames.Contains(name, StringComparer.Ordinal));

            if (!named)
            {
                yield return Violation(
                    workload,
                    $"{workload.Kind}/{workload.Name} exposes no container port named 'http' or 'metrics'.",
                    containers[0].YamlPath + ".ports");
            }
        }
    }
}

/// <summary>CP-OBS-004 — nobody is named as the owner of this workload.</summary>
internal sealed class OwnershipAnnotationCheck : WorkloadCheckBase
{
    public override CheckDescriptor Descriptor { get; } = new(
        "CP-OBS-004",
        "Workload declares no owner or contact",
        CheckCategory.Operability,
        Severity.Info,
        "At 03:00 the first question is who owns this. An owner annotation on the workload answers it from the "
        + "cluster itself, without anyone having to find the right wiki page or guess from the namespace name.",
        "metadata:\n  annotations:\n    chartpilot.io/owner: team-member-platform\n"
        + "    chartpilot.io/contact: \"#team-member-platform\"",
        "https://kubernetes.io/docs/concepts/overview/working-with-objects/annotations/");

    public override IEnumerable<Finding> Evaluate(CheckContext context)
    {
        foreach (var workload in CheckHelpers.Workloads(context))
        {
            var annotations = ManifestNavigator.GetAnnotations(workload);
            var labels = ManifestNavigator.GetLabels(workload);

            var declared = CheckHelpers.OwnershipAnnotations.Any(annotations.ContainsKey)
                           || CheckHelpers.OwnershipLabels.Any(labels.ContainsKey);

            if (!declared)
            {
                yield return Violation(
                    workload,
                    $"{workload.Kind}/{workload.Name} carries no ownership or contact annotation.",
                    "metadata.annotations");
            }
        }
    }
}

/// <summary>CP-OBS-005 — nothing configures how the workload logs.</summary>
internal sealed class LoggingConfigurationCheck : WorkloadCheckBase
{
    public override CheckDescriptor Descriptor { get; } = new(
        "CP-OBS-005",
        "Workload declares no logging configuration",
        CheckCategory.Operability,
        Severity.Info,
        "Log level and log format are the two settings an operator needs to change during an incident and cannot "
        + "change at all if the chart never exposes them. A workload that logs at whatever level its image was "
        + "built with is either too quiet to debug or too loud to afford.",
        "env:\n  - name: LOG_LEVEL\n    value: info\n  - name: LOG_FORMAT\n    value: json",
        "https://kubernetes.io/docs/concepts/cluster-administration/logging/");

    /// <summary>Environment variable names, matched case-insensitively, that configure logging.</summary>
    private static readonly string[] LoggingVariables =
    [
        "LOG_LEVEL", "LOG_FORMAT", "LOGLEVEL", "LOGGING_LEVEL", "LOG_OUTPUT", "RUST_LOG",
        "LOGGING__LOGLEVEL__DEFAULT", "SERILOG__MINIMUMLEVEL", "JAVA_LOGGING_LEVEL", "GIN_MODE"
    ];

    public override IEnumerable<Finding> Evaluate(CheckContext context)
    {
        foreach (var workload in CheckHelpers.LongLivedWorkloads(context))
        {
            var configured = false;

            foreach (var container in CheckHelpers.AppContainers(workload))
            {
                foreach (var env in ManifestNavigator.GetSequence(container.Node, "env"))
                {
                    var name = ManifestNavigator.GetString(env, "name");

                    if (name is not null && Configures(name))
                    {
                        configured = true;
                        break;
                    }
                }

                if (configured)
                {
                    break;
                }
            }

            if (!configured)
            {
                yield return Violation(
                    workload,
                    $"{workload.Kind}/{workload.Name} sets no logging environment variable "
                    + "(LOG_LEVEL, LOG_FORMAT or an equivalent).",
                    CheckHelpers.PodSpecField(workload, "containers[0].env"));
            }
        }
    }

    private static bool Configures(string name)
    {
        foreach (var variable in LoggingVariables)
        {
            if (string.Equals(name, variable, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return name.Contains("LOG_LEVEL", StringComparison.OrdinalIgnoreCase)
               || name.Contains("LOG_FORMAT", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>CP-OBS-006 — nothing lets a request be followed across services.</summary>
internal sealed class CorrelationIdConfigurationCheck : WorkloadCheckBase
{
    public override CheckDescriptor Descriptor { get; } = new(
        "CP-OBS-006",
        "Chart configures no request correlation",
        CheckCategory.Operability,
        Severity.Info,
        "Metrics say something is wrong and logs say what one service saw. Only a correlation or trace id joins "
        + "them into one story across the mesh, and it has to be configured in the chart because it is per-service "
        + "configuration, not a cluster-wide switch.",
        "env:\n  - name: OTEL_SERVICE_NAME\n    value: my-app\n"
        + "  - name: OTEL_EXPORTER_OTLP_ENDPOINT\n    value: http://otel-collector.observability:4317\n"
        + "# or propagate the header your platform standardises on:\n"
        + "  - name: CORRELATION_ID_HEADER\n    value: X-Correlation-Id",
        "https://opentelemetry.io/docs/languages/sdk-configuration/general/");

    /// <summary>Fragments of an environment variable name that indicate trace or correlation configuration.</summary>
    private static readonly string[] CorrelationMarkers =
    [
        "OTEL_", "OTLP", "TRACING", "TRACE_", "_TRACE", "CORRELATION", "REQUEST_ID", "JAEGER", "ZIPKIN", "B3_"
    ];

    /// <summary>Annotations Istio and the OpenTelemetry operator use to turn tracing on.</summary>
    private static readonly string[] CorrelationAnnotations =
    [
        "sidecar.istio.io/inject", "proxy.istio.io/config", "instrumentation.opentelemetry.io/inject-sdk"
    ];

    public override IEnumerable<Finding> Evaluate(CheckContext context)
    {
        if (context.Graph.Resources.Any(HasCorrelationAnnotation)
            || CheckHelpers.LongLivedWorkloads(context).Any(HasCorrelationEnv))
        {
            yield break;
        }

        yield return ChartViolation(
            "No workload configures tracing or a correlation id (no OTEL_* variable, no tracing annotation), "
            + "so a request cannot be followed from one service to the next.");
    }

    private static bool HasCorrelationAnnotation(RenderedResource resource)
    {
        var annotations = ManifestNavigator.GetAnnotations(resource);

        foreach (var annotation in CorrelationAnnotations)
        {
            if (annotations.ContainsKey(annotation))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasCorrelationEnv(RenderedResource workload)
    {
        foreach (var container in ManifestNavigator.GetContainers(workload))
        {
            foreach (var env in ManifestNavigator.GetSequence(container.Node, "env"))
            {
                var name = ManifestNavigator.GetString(env, "name");

                if (name is null)
                {
                    continue;
                }

                foreach (var marker in CorrelationMarkers)
                {
                    if (name.Contains(marker, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }
}
