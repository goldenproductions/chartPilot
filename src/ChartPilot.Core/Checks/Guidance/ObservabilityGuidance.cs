namespace ChartPilot.Core.Checks.Guidance;

/// <summary>Authored guidance for the CP-OBS-* family.</summary>
internal static class ObservabilityGuidance
{
    public static IEnumerable<KeyValuePair<string, CheckGuidance>> Entries()
    {
        yield return new("CP-OBS-001", new(
            "Nothing tells Prometheus to scrape this service. Without metrics it has no SLO, no alerting and no "
            + "capacity data — during an incident the only evidence available is logs, and afterwards there is no "
            + "way to say whether it got slower.",
            [
                new FixOption(
                    "Declare a ServiceMonitor",
                    "Let the Prometheus Operator discover the service from the chart.",
                    "apiVersion: monitoring.coreos.com/v1\nkind: ServiceMonitor\nmetadata:\n  name: member-api\n  labels:\n    release: kube-prometheus-stack   # must match your Prometheus's selector\nspec:\n  selector:\n    matchLabels:\n      app.kubernetes.io/name: member-api\n  endpoints:\n    - port: metrics\n      path: /metrics\n      interval: 30s",
                    "The right answer where the Prometheus Operator is installed. The label on the ServiceMonitor "
                    + "must match what your Prometheus selects, or it is silently ignored.",
                    IsRecommended: true),
                new FixOption(
                    "Use scrape annotations",
                    "For a Prometheus configured with the classic pod-annotation discovery.",
                    "podAnnotations:\n  prometheus.io/scrape: \"true\"\n  prometheus.io/port: \"8080\"\n  prometheus.io/path: \"/metrics\"",
                    "Works without the operator and needs no extra resource. Less expressive — no relabelling, no "
                    + "per-endpoint interval — and it depends on how your Prometheus is configured.",
                    false),
                new FixOption(
                    "It has no metrics to expose",
                    "Some jobs genuinely have nothing to scrape.",
                    "# .chartpilot.yaml\nsuppress:\n  - id: CP-OBS-001\n    reason: \"Nightly batch job; success and duration are reported through the Job's own status.\"\n    expires: 2027-01-01",
                    "Reasonable for short-lived Jobs. For anything long-running that serves requests, the honest "
                    + "answer is usually that metrics have not been added yet.",
                    false)
            ]));

        yield return new("CP-OBS-002", new(
            "The resource is missing the standard app.kubernetes.io labels. Those labels are the join key that "
            + "dashboards, alert routes, cost reports and incident queries all rely on — a resource without them "
            + "is invisible to every one of those tools.",
            [
                new FixOption(
                    "Apply the standard label set everywhere",
                    "Define them once in the chart's helper and include them on every resource.",
                    "{{- define \"member-api.labels\" -}}\nhelm.sh/chart: {{ include \"member-api.chart\" . }}\napp.kubernetes.io/name: {{ include \"member-api.name\" . }}\napp.kubernetes.io/instance: {{ .Release.Name }}\napp.kubernetes.io/version: {{ .Chart.AppVersion | quote }}\napp.kubernetes.io/managed-by: {{ .Release.Service }}\napp.kubernetes.io/part-of: member-platform\n{{- end }}\n\n# then on each resource:\nmetadata:\n  labels:\n    {{- include \"member-api.labels\" . | nindent 4 }}",
                    "The standard Helm pattern, and the only version that stays consistent as the chart grows. A "
                    + "chart created by `helm create` already has this helper — resources added by hand often miss it.",
                    IsRecommended: true),
                new FixOption(
                    "Label this resource directly",
                    "Add the labels to the one resource that is missing them.",
                    "metadata:\n  labels:\n    app.kubernetes.io/name: member-api\n    app.kubernetes.io/instance: member-api\n    app.kubernetes.io/version: \"1.12.0\"\n    app.kubernetes.io/managed-by: Helm\n    app.kubernetes.io/part-of: member-platform",
                    "Fine as a quick fix. Be careful changing labels on an existing Deployment: selector labels "
                    + "are immutable, so altering those means replacing the object.",
                    false)
            ]));

        yield return new("CP-OBS-003", new(
            "The workload's ports have no names, or none called http or metrics. Named ports are what let a "
            + "Service, a probe and a ServiceMonitor all refer to the same port without repeating the number — "
            + "so changing where the app listens becomes one edit instead of four.",
            [
                new FixOption(
                    "Name the ports",
                    "Name them in the container, then refer to the names everywhere else.",
                    "ports:\n  - name: http\n    containerPort: 8080\n  - name: metrics\n    containerPort: 9090\n\nreadinessProbe:\n  httpGet:\n    port: http      # not 8080",
                    "Costs nothing and removes a whole class of copy-paste mistake. Port names are limited to 15 "
                    + "characters and must be lowercase.",
                    IsRecommended: true)
            ]));

        yield return new("CP-OBS-004", new(
            "Nothing on this workload says who owns it. At three in the morning the first question is who to "
            + "call, and an annotation answers it from the cluster itself rather than from a wiki page somebody "
            + "has to find.",
            [
                new FixOption(
                    "Annotate the owner and a contact",
                    "Put the team and a reachable channel on the workload.",
                    "podAnnotations:\n  chartpilot.io/owner: team-member-platform\n  chartpilot.io/contact: \"#team-member-platform\"",
                    "Use a channel or rota rather than a person: individuals change teams and the annotation does "
                    + "not follow them.",
                    IsRecommended: true),
                new FixOption(
                    "Use a label so it is queryable",
                    "A label can be selected on, which an annotation cannot.",
                    "metadata:\n  labels:\n    app.kubernetes.io/part-of: member-platform\n    owner: team-member-platform",
                    "Better when you want to list everything a team owns with `kubectl get -l`. Label values are "
                    + "restricted, so a Slack channel or email needs to stay in an annotation.",
                    false)
            ]));

        yield return new("CP-OBS-005", new(
            "The workload sets no logging configuration. Log level and format are the two things an operator "
            + "wants to change during an incident and cannot change at all if the chart never exposes them — so "
            + "the service logs at whatever its image was built with: too quiet to debug, or too loud to afford.",
            [
                new FixOption(
                    "Expose level and format as values",
                    "Make them configurable per environment.",
                    "env:\n  - name: LOG_LEVEL\n    value: {{ .Values.logging.level | default \"info\" }}\n  - name: LOG_FORMAT\n    value: {{ .Values.logging.format | default \"json\" }}\n\n# values-dev.yaml\nlogging:\n  level: debug",
                    "Structured JSON in production and debug in dev is the usual split. Exposing it as a value "
                    + "means raising the level during an incident is a values change, not a rebuild.",
                    IsRecommended: true),
                new FixOption(
                    "Set them as fixed environment variables",
                    "Simplest version, when per-environment control is not needed.",
                    "env:\n  - name: LOG_LEVEL\n    value: info\n  - name: LOG_FORMAT\n    value: json",
                    "Better than nothing and honest about the intent. Changing the level then requires editing the "
                    + "chart and redeploying.",
                    false)
            ]));

        yield return new("CP-OBS-006", new(
            "No workload in the chart configures tracing or a correlation id. Metrics tell you something is "
            + "wrong and logs tell you what one service saw; only a shared id joins them into a single story "
            + "across the services a request passed through.",
            [
                new FixOption(
                    "Configure OpenTelemetry",
                    "Point the workload at a collector and give it a service name.",
                    "env:\n  - name: OTEL_SERVICE_NAME\n    value: member-api\n  - name: OTEL_EXPORTER_OTLP_ENDPOINT\n    value: http://otel-collector.observability:4317\n  - name: OTEL_TRACES_SAMPLER\n    value: parentbased_traceidratio\n  - name: OTEL_TRACES_SAMPLER_ARG\n    value: \"0.1\"",
                    "The standard approach, and it works with any OTLP backend. Sample well below 100% in "
                    + "production unless you have measured what full sampling costs.",
                    IsRecommended: true),
                new FixOption(
                    "Propagate a correlation header",
                    "Simpler: agree on one header and carry it through logs.",
                    "env:\n  - name: CORRELATION_ID_HEADER\n    value: X-Correlation-Id",
                    "Much less work than tracing and it solves the common case of joining logs across services. "
                    + "It gives you no timing information — that still needs traces.",
                    false)
            ]));
    }
}
