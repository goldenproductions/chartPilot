namespace ChartPilot.Api.Tests;

/// <summary>
/// A real chart directory on disk, built for the test run. The chart loader and the values
/// validator are the production ones, so the chart has to exist; only Helm is stubbed.
/// </summary>
internal sealed class TestChart : IDisposable
{
    public TestChart()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "chartpilot-api-tests",
            Guid.NewGuid().ToString("n"));

        Directory.CreateDirectory(Path);
        Directory.CreateDirectory(System.IO.Path.Combine(Path, "templates"));

        Write("Chart.yaml", """
            apiVersion: v2
            name: member-api
            description: A chart used by the ChartPilot API contract tests
            type: application
            version: 0.3.1
            appVersion: "1.12.0"
            """);

        Write("values.yaml", """
            replicaCount: 1
            image:
              repository: ghcr.io/example/member-api
              tag: latest
            """);

        Write("values-dev.yaml", """
            replicaCount: 1
            """);

        Write("values-prod.yaml", """
            replicaCount: 3
            """);

        Write("values.schema.json", """
            {
              "$schema": "https://json-schema.org/draft/2020-12/schema",
              "type": "object",
              "properties": {
                "replicaCount": {
                  "type": "integer",
                  "minimum": 1
                }
              },
              "required": ["replicaCount"]
            }
            """);

        Write(System.IO.Path.Combine("templates", "deployment.yaml"), """
            apiVersion: apps/v1
            kind: Deployment
            metadata:
              name: {{ .Release.Name }}
            """);

        Write(System.IO.Path.Combine("templates", "service.yaml"), """
            apiVersion: v1
            kind: Service
            metadata:
              name: {{ .Release.Name }}
            """);
    }

    public string Path { get; }

    private void Write(string relativePath, string content)
        => File.WriteAllText(System.IO.Path.Combine(Path, relativePath), content);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
        catch (IOException)
        {
            // Leftover temp directories are the OS's problem, not the test's.
        }
    }
}
