using ChartPilot.Core.Helm;

namespace ChartPilot.Api.Tests;

/// <summary>
/// A canned Helm. The API contract tests are about routes, status codes and JSON shape, so nothing
/// here starts a process — which also means the suite runs on a machine without Helm installed.
/// </summary>
internal sealed class StubHelmClient : IHelmClient
{
    public const string Manifests = """
        ---
        # Source: member-api/templates/deployment.yaml
        apiVersion: apps/v1
        kind: Deployment
        metadata:
          name: member-api
          labels:
            app.kubernetes.io/name: member-api
        spec:
          replicas: 1
          selector:
            matchLabels:
              app.kubernetes.io/name: member-api
          template:
            metadata:
              labels:
                app.kubernetes.io/name: member-api
            spec:
              containers:
                - name: member-api
                  image: ghcr.io/example/member-api:latest
        ---
        # Source: member-api/templates/service.yaml
        apiVersion: v1
        kind: Service
        metadata:
          name: member-api
        spec:
          selector:
            app.kubernetes.io/name: member-api
          ports:
            - port: 80
              targetPort: 8080
        """;

    public bool Available { get; set; } = true;

    public bool ThrowNotAvailable { get; set; }

    public HelmTemplateResult TemplateResult { get; set; } =
        new(true, Manifests, string.Empty, 0, TimeSpan.FromMilliseconds(12), false, false);

    public Task<HelmExecutable> ResolveAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(Available
            ? new HelmExecutable(true, "/usr/bin/helm", "v4.2.4", null, "path")
            : new HelmExecutable(false, null, null, "helm was not found on PATH.", "not-found"));

    public Task<HelmTemplateResult> TemplateAsync(HelmTemplateRequest request, CancellationToken cancellationToken = default)
    {
        if (ThrowNotAvailable)
        {
            throw new HelmNotAvailableException("helm was not found on PATH.");
        }

        return Task.FromResult(TemplateResult);
    }

    public Task<HelmLintResult> LintAsync(string chartPath, IReadOnlyList<string> valuesFiles, CancellationToken cancellationToken = default)
    {
        if (ThrowNotAvailable)
        {
            throw new HelmNotAvailableException("helm was not found on PATH.");
        }

        return Task.FromResult(new HelmLintResult(true, [], string.Empty, 0));
    }
}
