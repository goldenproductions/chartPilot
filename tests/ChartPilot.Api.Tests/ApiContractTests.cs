using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ChartPilot.Core.Helm;

namespace ChartPilot.Api.Tests;

/// <summary>
/// Contract tests for every route in docs/architecture.md §7: the status code, the JSON shape, and
/// the ProblemDetails vocabulary the GUI branches on.
/// </summary>
public sealed class ApiContractTests : IDisposable
{
    private const string Base = "/api/v1";

    private readonly ChartPilotApiFactory _factory = new();
    private readonly TestChart _chart = new();
    private readonly HttpClient _client;

    public ApiContractTests() => _client = _factory.CreateClient();

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
        _chart.Dispose();
    }

    private async Task<JsonElement> CreateWorkspaceAsync()
    {
        var response = await _client.PostAsJsonAsync($"{Base}/workspaces", new { chartPath = _chart.Path });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return await ReadJsonAsync(response);
    }

    private async Task<string> CreateWorkspaceIdAsync()
        => (await CreateWorkspaceAsync()).GetProperty("workspaceId").GetString()!;

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    [Fact]
    public async Task Get_environment_reports_the_resolved_helm()
    {
        var response = await _client.GetAsync($"{Base}/environment");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await ReadJsonAsync(response);

        Assert.True(body.GetProperty("helmAvailable").GetBoolean());
        Assert.Equal("v4.2.4", body.GetProperty("helmVersion").GetString());
        Assert.Equal("path", body.GetProperty("resolutionSource").GetString());
    }

    [Fact]
    public async Task Get_environment_reports_a_missing_helm_without_failing()
    {
        _factory.Helm.Available = false;

        var body = await ReadJsonAsync(await _client.GetAsync($"{Base}/environment"));

        Assert.False(body.GetProperty("helmAvailable").GetBoolean());
        Assert.Contains("not found", body.GetProperty("helmError").GetString()!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Post_workspaces_returns_the_chart_overview()
    {
        var body = await CreateWorkspaceAsync();

        Assert.Equal("member-api", body.GetProperty("name").GetString());
        Assert.Equal("0.3.1", body.GetProperty("version").GetString());
        Assert.Equal("1.12.0", body.GetProperty("appVersion").GetString());
        Assert.True(body.GetProperty("hasValuesSchema").GetBoolean());
        Assert.False(string.IsNullOrEmpty(body.GetProperty("workspaceId").GetString()));

        var valuesFiles = body.GetProperty("valuesFiles").EnumerateArray()
            .Select(v => v.GetProperty("fileName").GetString())
            .ToList();

        Assert.Contains("values.yaml", valuesFiles);
        Assert.Contains("values-prod.yaml", valuesFiles);
    }

    [Fact]
    public async Task Post_workspaces_rejects_a_directory_that_is_not_a_chart()
    {
        var response = await _client.PostAsJsonAsync(
            $"{Base}/workspaces",
            new { chartPath = Path.GetTempPath() });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await ReadJsonAsync(response);

        Assert.Equal("Not a chart directory", problem.GetProperty("title").GetString());
        Assert.Equal(400, problem.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task Post_workspaces_resolves_a_relative_chart_path_against_the_allowlist_root()
    {
        // The GUI shows the allowlist root and lets the user type a path relative to it, so a
        // relative path must not be resolved against the server's working directory.
        var relative = Path.GetRelativePath(Path.GetTempPath(), _chart.Path).Replace(Path.DirectorySeparatorChar, '/');

        var response = await _client.PostAsJsonAsync($"{Base}/workspaces", new { chartPath = relative });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await ReadJsonAsync(response);

        Assert.Equal("member-api", body.GetProperty("name").GetString());
    }

    [Fact]
    public async Task Post_workspaces_rejects_a_chart_outside_the_allowlist_root()
    {
        var outside = Path.GetPathRoot(Path.GetTempPath())!;

        var response = await _client.PostAsJsonAsync($"{Base}/workspaces", new { chartPath = outside });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await ReadJsonAsync(response);
        var title = problem.GetProperty("title").GetString();

        // A drive root is neither a chart nor inside the allowlist root; either refusal is correct,
        // and both are 400 with an actionable title.
        Assert.True(
            title is "Not a chart directory" or "Chart is outside the allowlist root",
            $"Unexpected title '{title}'.");
    }

    [Fact]
    public async Task Get_environment_reports_the_allowlist_root()
    {
        var response = await _client.GetAsync($"{Base}/environment");

        var body = await ReadJsonAsync(response);
        var root = body.GetProperty("allowlistRoot").GetString();

        Assert.False(string.IsNullOrWhiteSpace(root));
        Assert.StartsWith(
            Path.TrimEndingDirectorySeparator(Path.GetTempPath()),
            Path.TrimEndingDirectorySeparator(root!),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Post_workspaces_requires_a_chart_path()
    {
        var response = await _client.PostAsJsonAsync($"{Base}/workspaces", new { chartPath = (string?)null });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Invalid request", (await ReadJsonAsync(response)).GetProperty("title").GetString());
    }

    [Fact]
    public async Task Get_workspace_returns_the_same_chart_it_was_opened_with()
    {
        var id = await CreateWorkspaceIdAsync();

        var response = await _client.GetAsync($"{Base}/workspaces/{id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(id, (await ReadJsonAsync(response)).GetProperty("workspaceId").GetString());
    }

    [Fact]
    public async Task An_unknown_workspace_is_a_404_problem_document()
    {
        var response = await _client.GetAsync($"{Base}/workspaces/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var problem = await ReadJsonAsync(response);

        Assert.Equal("Workspace not found", problem.GetProperty("title").GetString());
        Assert.Equal(404, problem.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task Get_values_returns_the_default_file_and_a_named_file()
    {
        var id = await CreateWorkspaceIdAsync();

        var defaultValues = await ReadJsonAsync(await _client.GetAsync($"{Base}/workspaces/{id}/values"));

        Assert.Equal("values.yaml", defaultValues.GetProperty("source").GetString());
        Assert.False(defaultValues.GetProperty("isDraft").GetBoolean());
        Assert.Contains("replicaCount: 1", defaultValues.GetProperty("yaml").GetString()!, StringComparison.Ordinal);

        var prod = await ReadJsonAsync(
            await _client.GetAsync($"{Base}/workspaces/{id}/values?file=values-prod.yaml"));

        Assert.Contains("replicaCount: 3", prod.GetProperty("yaml").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Get_values_rejects_a_file_the_chart_does_not_ship()
    {
        var id = await CreateWorkspaceIdAsync();

        var response = await _client.GetAsync($"{Base}/workspaces/{id}/values?file=../../../etc/passwd");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Put_values_stores_the_draft_and_reports_it_as_valid()
    {
        var id = await CreateWorkspaceIdAsync();

        var response = await _client.PutAsJsonAsync(
            $"{Base}/workspaces/{id}/values",
            new { yaml = "replicaCount: 4\n" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await ReadJsonAsync(response);

        Assert.True(body.GetProperty("stored").GetBoolean());
        Assert.True(body.GetProperty("isValid").GetBoolean());
        Assert.Empty(body.GetProperty("issues").EnumerateArray());

        var draft = await ReadJsonAsync(await _client.GetAsync($"{Base}/workspaces/{id}/values"));

        Assert.True(draft.GetProperty("isDraft").GetBoolean());
        Assert.Contains("replicaCount: 4", draft.GetProperty("yaml").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Put_values_returns_schema_issues_but_still_stores_the_draft()
    {
        var id = await CreateWorkspaceIdAsync();

        var response = await _client.PutAsJsonAsync(
            $"{Base}/workspaces/{id}/values",
            new { yaml = "replicaCount: \"three\"\n" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await ReadJsonAsync(response);

        Assert.True(body.GetProperty("stored").GetBoolean());
        Assert.False(body.GetProperty("isValid").GetBoolean());
        Assert.NotEmpty(body.GetProperty("issues").EnumerateArray());

        var draft = await ReadJsonAsync(await _client.GetAsync($"{Base}/workspaces/{id}/values"));

        Assert.True(draft.GetProperty("isDraft").GetBoolean());
    }

    [Fact]
    public async Task Put_values_on_an_unknown_workspace_is_a_404()
    {
        var response = await _client.PutAsJsonAsync(
            $"{Base}/workspaces/nope/values",
            new { yaml = "replicaCount: 1\n" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_render_returns_the_parsed_resources_and_the_raw_manifests()
    {
        var id = await CreateWorkspaceIdAsync();

        var response = await _client.PostAsJsonAsync(
            $"{Base}/workspaces/{id}/render",
            new { valuesFiles = new[] { "values-prod.yaml" } });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await ReadJsonAsync(response);

        Assert.Equal(2, body.GetProperty("resourceCount").GetInt32());

        var kinds = body.GetProperty("resources").EnumerateArray()
            .Select(r => r.GetProperty("kind").GetString())
            .ToList();

        Assert.Contains("Deployment", kinds);
        Assert.Contains("Service", kinds);

        var categories = body.GetProperty("resources").EnumerateArray()
            .Select(r => r.GetProperty("category").GetString())
            .ToList();

        Assert.Contains("Workloads", categories);
        Assert.Contains("Networking", categories);
        Assert.Contains("kind: Deployment", body.GetProperty("rawManifests").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Post_render_rejects_a_values_file_the_chart_does_not_ship()
    {
        var id = await CreateWorkspaceIdAsync();

        var response = await _client.PostAsJsonAsync(
            $"{Base}/workspaces/{id}/render",
            new { valuesFiles = new[] { "values-nope.yaml" } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Post_render_surfaces_a_helm_failure_as_a_problem_document_with_stderr()
    {
        var id = await CreateWorkspaceIdAsync();

        _factory.Helm.TemplateResult = new HelmTemplateResult(
            false,
            string.Empty,
            "Error: template: member-api/templates/deployment.yaml:12:14: nil pointer",
            1,
            TimeSpan.Zero,
            false,
            false);

        var response = await _client.PostAsJsonAsync($"{Base}/workspaces/{id}/render", new { });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        var problem = await ReadJsonAsync(response);

        Assert.Equal("Review failed", problem.GetProperty("title").GetString());
        Assert.Contains("nil pointer", problem.GetProperty("helmStderr").GetString()!, StringComparison.Ordinal);
        Assert.Equal("member-api/templates/deployment.yaml:12:14", problem.GetProperty("source").GetString());
    }

    [Fact]
    public async Task A_missing_helm_binary_is_a_503_carrying_the_install_command()
    {
        var id = await CreateWorkspaceIdAsync();
        _factory.Helm.ThrowNotAvailable = true;

        var response = await _client.PostAsJsonAsync($"{Base}/workspaces/{id}/render", new { });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

        var problem = await ReadJsonAsync(response);

        Assert.Equal("Helm is not available", problem.GetProperty("title").GetString());
        Assert.Contains("winget install Helm.Helm", problem.GetProperty("detail").GetString()!, StringComparison.Ordinal);
        Assert.Equal("winget install Helm.Helm", problem.GetProperty("installCommand").GetString());
    }

    [Fact]
    public async Task Post_review_returns_findings_and_a_score()
    {
        var id = await CreateWorkspaceIdAsync();

        var response = await _client.PostAsJsonAsync(
            $"{Base}/workspaces/{id}/review",
            new { valuesFiles = new[] { "values-prod.yaml" }, environment = "prod" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await ReadJsonAsync(response);

        Assert.Equal("member-api", body.GetProperty("chartName").GetString());
        Assert.Equal("prod", body.GetProperty("environment").GetString());
        Assert.False(string.IsNullOrEmpty(body.GetProperty("profileId").GetString()));
        Assert.InRange(body.GetProperty("score").GetProperty("overall").GetInt32(), 0, 100);
        Assert.NotEmpty(body.GetProperty("resources").EnumerateArray());
        Assert.True(body.TryGetProperty("findings", out _));
        Assert.True(body.TryGetProperty("passed", out _));
        Assert.True(body.TryGetProperty("suppressed", out _));
        Assert.Equal("v4.2.4", body.GetProperty("helmVersion").GetString());
    }

    [Fact]
    public async Task Get_diff_compares_the_environment_values_files()
    {
        var id = await CreateWorkspaceIdAsync();

        var response = await _client.GetAsync(
            $"{Base}/workspaces/{id}/diff?files=values-dev.yaml&files=values-prod.yaml&differencesOnly=true");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await ReadJsonAsync(response);

        Assert.Equal(
            new[] { "values-dev.yaml", "values-prod.yaml" },
            body.GetProperty("sources").EnumerateArray().Select(s => s.GetString()).ToArray());

        Assert.Contains(
            body.GetProperty("rows").EnumerateArray(),
            row => row.GetProperty("path").GetString() == "replicaCount");
    }

    [Fact]
    public async Task Get_diff_needs_at_least_two_documents()
    {
        var id = await CreateWorkspaceIdAsync();

        var response = await _client.GetAsync($"{Base}/workspaces/{id}/diff?files=values-dev.yaml");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Post_report_returns_pasteable_markdown()
    {
        var id = await CreateWorkspaceIdAsync();

        var response = await _client.PostAsJsonAsync($"{Base}/workspaces/{id}/report", new { environment = "prod" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/markdown", response.Content.Headers.ContentType?.MediaType);

        var markdown = await response.Content.ReadAsStringAsync();

        Assert.StartsWith("# ChartPilot Review: member-api", markdown, StringComparison.Ordinal);
        Assert.Contains("## Recommended actions", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Post_workflow_returns_yaml_with_the_expressions_intact()
    {
        var id = await CreateWorkspaceIdAsync();

        var response = await _client.PostAsJsonAsync(
            $"{Base}/workspaces/{id}/workflow",
            new { profileId = "default", failOn = "critical", @namespace = "member-platform" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/yaml", response.Content.Headers.ContentType?.MediaType);

        var yaml = await response.Content.ReadAsStringAsync();

        Assert.Contains("name: Helm Deploy", yaml, StringComparison.Ordinal);
        Assert.Contains("${{ inputs.environment }}", yaml, StringComparison.Ordinal);
        Assert.Contains("- dev", yaml, StringComparison.Ordinal);
        Assert.Contains("- prod", yaml, StringComparison.Ordinal);
        Assert.Contains("--namespace member-platform", yaml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Get_profiles_lists_the_golden_paths()
    {
        var response = await _client.GetAsync($"{Base}/profiles");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await ReadJsonAsync(response);
        var profiles = body.EnumerateArray().ToList();

        Assert.NotEmpty(profiles);
        Assert.All(profiles, p => Assert.False(string.IsNullOrEmpty(p.GetProperty("id").GetString())));
        Assert.Contains(profiles, p => p.GetProperty("isDefault").GetBoolean());
        Assert.All(profiles, p => Assert.True(p.TryGetProperty("requirements", out _)));
    }

    [Fact]
    public async Task Get_checks_lists_the_rule_catalog()
    {
        var response = await _client.GetAsync($"{Base}/checks");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var checks = (await ReadJsonAsync(response)).EnumerateArray().ToList();

        Assert.NotEmpty(checks);
        Assert.All(checks, c => Assert.StartsWith("CP-", c.GetProperty("id").GetString()!, StringComparison.Ordinal));
        Assert.All(checks, c => Assert.False(string.IsNullOrEmpty(c.GetProperty("rationale").GetString())));
    }

    [Fact]
    public async Task An_unknown_api_route_is_a_problem_document_not_the_spa()
    {
        var response = await _client.GetAsync($"{Base}/nope");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Get_values_export_returns_the_draft_as_a_downloadable_file()
    {
        var id = await CreateWorkspaceIdAsync();

        await _client.PutAsJsonAsync($"{Base}/workspaces/{id}/values", new { yaml = "replicaCount: 7\n" });

        var response = await _client.GetAsync($"{Base}/workspaces/{id}/values/export");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("replicaCount: 7\n", await response.Content.ReadAsStringAsync());
        Assert.Equal("values.yaml", response.Content.Headers.ContentDisposition?.FileNameStar
                                    ?? response.Content.Headers.ContentDisposition?.FileName);
    }

    [Fact]
    public async Task Get_values_export_falls_back_to_the_file_on_disk()
    {
        var id = await CreateWorkspaceIdAsync();

        var response = await _client.GetAsync($"{Base}/workspaces/{id}/values/export?file=values-prod.yaml");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("replicaCount", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Get_values_export_rejects_a_file_the_chart_does_not_ship()
    {
        var id = await CreateWorkspaceIdAsync();

        var response = await _client.GetAsync($"{Base}/workspaces/{id}/values/export?file=values-nope.yaml");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    /// <summary>
    /// The review body carries the buffer it belongs to, so a result can never describe a draft that
    /// a later PUT has already replaced.
    /// </summary>
    [Fact]
    public async Task Post_review_uses_the_draft_carried_in_the_request_body()
    {
        var id = await CreateWorkspaceIdAsync();

        await _client.PutAsJsonAsync($"{Base}/workspaces/{id}/values", new { yaml = "replicaCount: 1\n" });

        var response = await _client.PostAsJsonAsync(
            $"{Base}/workspaces/{id}/review",
            new { draftValues = "replicaCount: 9\n" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // The request body is now the workspace draft as well, so the export agrees with the review.
        var exported = await _client.GetStringAsync($"{Base}/workspaces/{id}/values/export");
        Assert.Equal("replicaCount: 9\n", exported);
    }
}
