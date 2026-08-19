using ChartPilot.Core.Helm;
using ChartPilot.Helm;
using Microsoft.Extensions.Options;

namespace ChartPilot.Helm.Tests;

public sealed class HelmClientTests
{
    private static HelmTemplateRequest Request(string chartPath, params string[] valuesFiles) =>
        new(chartPath, "member-api", valuesFiles);

    [Fact]
    public async Task TemplateAsync_builds_the_expected_argument_list_for_a_plain_render()
    {
        using var harness = HelmTestHarness.Create();

        var result = await harness.Client.TemplateAsync(Request(harness.ChartPath));

        Assert.True(result.Success);
        Assert.Equal(
            new[] { "template", "member-api", PathGuard.Normalize(harness.ChartPath), "--include-crds", "--skip-tests" },
            harness.HelmArguments);
        Assert.Equal(harness.HelmExecutablePath, harness.HelmRequest.FileName);
    }

    [Fact]
    public async Task TemplateAsync_omits_include_crds_and_skip_tests_when_the_request_turns_them_off()
    {
        using var harness = HelmTestHarness.Create();

        await harness.Client.TemplateAsync(
            Request(harness.ChartPath) with { IncludeCrds = false, SkipTests = false });

        Assert.DoesNotContain("--include-crds", harness.HelmArguments);
        Assert.DoesNotContain("--skip-tests", harness.HelmArguments);
    }

    [Fact]
    public async Task TemplateAsync_does_not_pass_dependency_update_by_default()
    {
        using var harness = HelmTestHarness.Create();

        await harness.Client.TemplateAsync(Request(harness.ChartPath));

        Assert.DoesNotContain("--dependency-update", harness.HelmArguments);
    }

    [Fact]
    public async Task TemplateAsync_passes_dependency_update_when_the_user_opts_in()
    {
        using var harness = HelmTestHarness.Create();

        await harness.Client.TemplateAsync(Request(harness.ChartPath) with { DependencyUpdate = true });

        Assert.Contains("--dependency-update", harness.HelmArguments);
    }

    [Fact]
    public async Task TemplateAsync_never_passes_dry_run_or_a_kubeconfig()
    {
        using var harness = HelmTestHarness.Create();

        await harness.Client.TemplateAsync(Request(harness.ChartPath));

        Assert.DoesNotContain("--dry-run", harness.HelmArguments);
        Assert.DoesNotContain("--kubeconfig", harness.HelmArguments);

        var overrides = harness.HelmRequest.EnvironmentOverrides;
        Assert.True(overrides.ContainsKey("KUBECONFIG"));
        Assert.Null(overrides["KUBECONFIG"]);
    }

    [Fact]
    public async Task TemplateAsync_passes_kube_version_and_namespace_when_supplied()
    {
        using var harness = HelmTestHarness.Create();

        await harness.Client.TemplateAsync(
            Request(harness.ChartPath) with { KubeVersion = "1.30.0", Namespace = "member" });

        var arguments = harness.HelmArguments;
        var kubeVersionIndex = arguments.ToList().IndexOf("--kube-version");
        var namespaceIndex = arguments.ToList().IndexOf("--namespace");

        Assert.True(kubeVersionIndex >= 0);
        Assert.Equal("1.30.0", arguments[kubeVersionIndex + 1]);
        Assert.True(namespaceIndex >= 0);
        Assert.Equal("member", arguments[namespaceIndex + 1]);
    }

    [Fact]
    public async Task TemplateAsync_passes_every_values_file_in_order()
    {
        using var harness = HelmTestHarness.Create();
        var baseValues = harness.WriteValuesFile("chart/values.yaml");
        var prodValues = harness.WriteValuesFile("values-prod.yaml");

        await harness.Client.TemplateAsync(Request(harness.ChartPath, baseValues, prodValues));

        var arguments = harness.HelmArguments.ToList();
        var files = arguments
            .Select((value, index) => (value, index))
            .Where(pair => pair.value == "-f")
            .Select(pair => arguments[pair.index + 1])
            .ToList();

        Assert.Equal(new[] { PathGuard.Normalize(baseValues), PathGuard.Normalize(prodValues) }, files);
    }

    [Fact]
    public async Task TemplateAsync_resolves_a_bare_values_file_name_against_the_chart_directory()
    {
        using var harness = HelmTestHarness.Create();
        var chartValues = harness.WriteValuesFile("chart/values-dev.yaml");

        await harness.Client.TemplateAsync(Request(harness.ChartPath, "values-dev.yaml"));

        Assert.Contains(PathGuard.Normalize(chartValues), harness.HelmArguments);
    }

    [Fact]
    public async Task TemplateAsync_writes_the_draft_to_a_temp_file_passed_last_and_deletes_it_afterwards()
    {
        using var harness = HelmTestHarness.Create();
        var baseValues = harness.WriteValuesFile("chart/values.yaml");

        string? draftPath = null;
        string? draftContent = null;

        harness.OnHelmCall(request =>
        {
            draftPath = request.Arguments[^1];
            draftContent = File.ReadAllText(draftPath);
            return new ProcessResult(0, "kind: Deployment\n", string.Empty, TimeSpan.FromMilliseconds(10), false, false);
        });

        const string draft = "replicaCount: 5\nimage:\n  tag: \"1.4.2\"\n";
        await harness.Client.TemplateAsync(Request(harness.ChartPath, baseValues) with { DraftValuesYaml = draft });

        var arguments = harness.HelmArguments;
        Assert.Equal("-f", arguments[^2]);
        Assert.NotNull(draftPath);
        Assert.Equal(draft, draftContent);
        Assert.Contains("chartpilot", draftPath!, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(PathGuard.Normalize(baseValues), draftPath);

        // The draft file lives only for the duration of the render.
        Assert.False(File.Exists(draftPath));
        Assert.False(Directory.Exists(Path.GetDirectoryName(draftPath)!));
    }

    [Fact]
    public async Task TemplateAsync_deletes_the_draft_directory_even_when_helm_fails()
    {
        using var harness = HelmTestHarness.Create();

        string? draftPath = null;
        harness.OnHelmCall(request =>
        {
            draftPath = request.Arguments[^1];
            return new ProcessResult(1, string.Empty, "Error: template: chart/templates/x.yaml:4:12", TimeSpan.FromMilliseconds(10), false, false);
        });

        var result = await harness.Client.TemplateAsync(
            Request(harness.ChartPath) with { DraftValuesYaml = "replicaCount: 2\n" });

        Assert.False(result.Success);
        Assert.NotNull(draftPath);
        Assert.False(Directory.Exists(Path.GetDirectoryName(draftPath)!));
    }

    [Fact]
    public async Task TemplateAsync_rejects_a_chart_path_outside_the_allowlist_root()
    {
        using var harness = HelmTestHarness.Create();

        var outside = Path.Combine(harness.ChartPath, "..", "..", "somewhere-else");

        await Assert.ThrowsAsync<ArgumentException>(
            () => harness.Client.TemplateAsync(Request(outside)));
    }

    [Fact]
    public async Task TemplateAsync_rejects_a_values_file_outside_the_allowlist_root()
    {
        using var harness = HelmTestHarness.Create();

        var outside = Path.Combine(harness.Root, "..", "escaped-values.yaml");

        await Assert.ThrowsAsync<ArgumentException>(
            () => harness.Client.TemplateAsync(Request(harness.ChartPath, outside)));
    }

    [Fact]
    public async Task TemplateAsync_rejects_a_release_name_that_looks_like_a_flag()
    {
        using var harness = HelmTestHarness.Create();

        await Assert.ThrowsAsync<ArgumentException>(
            () => harness.Client.TemplateAsync(new HelmTemplateRequest(harness.ChartPath, "--set=evil", [])));
    }

    [Fact]
    public async Task TemplateAsync_falls_back_to_the_configured_default_release_name()
    {
        using var harness = HelmTestHarness.Create();
        harness.Options.DefaultReleaseName = "chartpilot";

        await harness.Client.TemplateAsync(new HelmTemplateRequest(harness.ChartPath, "  ", []));

        Assert.Equal("chartpilot", harness.HelmArguments[1]);
    }

    [Fact]
    public async Task TemplateAsync_reports_a_failed_render_instead_of_throwing()
    {
        using var harness = HelmTestHarness.Create();
        const string stderr = "Error: template: chart/templates/deployment.yaml:12:18: executing \"chart/templates/deployment.yaml\"";
        harness.HelmResult = new ProcessResult(1, string.Empty, stderr, TimeSpan.FromMilliseconds(20), false, false);

        var result = await harness.Client.TemplateAsync(Request(harness.ChartPath));

        Assert.False(result.Success);
        Assert.Equal(1, result.ExitCode);
        Assert.Equal(stderr, result.StdErr);
    }

    [Fact]
    public async Task TemplateAsync_surfaces_a_timeout_as_a_failed_result()
    {
        using var harness = HelmTestHarness.Create();
        harness.Options.TemplateTimeout = TimeSpan.FromSeconds(2);
        harness.HelmResult = new ProcessResult(-1, string.Empty, string.Empty, TimeSpan.FromSeconds(2), true, false);

        var result = await harness.Client.TemplateAsync(Request(harness.ChartPath));

        Assert.True(result.TimedOut);
        Assert.False(result.Success);
        Assert.Contains("timed out", result.StdErr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TemplateAsync_propagates_truncation()
    {
        using var harness = HelmTestHarness.Create();
        harness.HelmResult = new ProcessResult(0, "kind: Deployment\n", string.Empty, TimeSpan.FromMilliseconds(30), false, true);

        var result = await harness.Client.TemplateAsync(Request(harness.ChartPath));

        Assert.True(result.OutputTruncated);
        Assert.True(result.Success);
    }

    [Fact]
    public async Task TemplateAsync_propagates_cancellation()
    {
        using var harness = HelmTestHarness.Create();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => harness.Client.TemplateAsync(Request(harness.ChartPath), cts.Token));
    }

    [Fact]
    public async Task TemplateAsync_throws_HelmNotAvailableException_when_helm_cannot_be_located()
    {
        using var harness = HelmTestHarness.Create();

        var options = new ChartPilotHelmOptions
        {
            HelmPath = Path.Combine(harness.Root, "missing", "helm.exe"),
            AllowlistRoot = harness.Root
        };

        var locator = new HelmLocator(harness.Runner, MicrosoftOptions.Create(options), null, new FakeHelmEnvironment { RestrictToRoot = harness.Root });
        var client = new HelmClient(harness.Runner, locator, MicrosoftOptions.Create(options));

        var exception = await Assert.ThrowsAsync<HelmNotAvailableException>(
            () => client.TemplateAsync(Request(harness.ChartPath)));

        Assert.Contains("winget install Helm.Helm", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LintAsync_builds_the_expected_arguments_and_parses_the_output()
    {
        using var harness = HelmTestHarness.Create();
        var values = harness.WriteValuesFile("chart/values.yaml");

        harness.HelmResult = new ProcessResult(
            0,
            """
            ==> Linting ./chart
            [INFO] Chart.yaml: icon is recommended
            [WARNING] templates/deployment.yaml: object name does not conform to Kubernetes naming requirements

            1 chart(s) linted, 0 chart(s) failed
            """,
            string.Empty,
            TimeSpan.FromMilliseconds(50),
            false,
            false);

        var result = await harness.Client.LintAsync(harness.ChartPath, [values]);

        Assert.Equal(
            new[] { "lint", PathGuard.Normalize(harness.ChartPath), "-f", PathGuard.Normalize(values) },
            harness.HelmArguments);
        Assert.True(result.Success);
        Assert.Equal(2, result.Messages.Count);
        Assert.Equal(HelmLintSeverity.Info, result.Messages[0].Severity);
        Assert.Equal("Chart.yaml", result.Messages[0].File);
        Assert.Equal(HelmLintSeverity.Warning, result.Messages[1].Severity);
    }

    [Fact]
    public async Task LintAsync_reports_failure_without_throwing()
    {
        using var harness = HelmTestHarness.Create();

        harness.HelmResult = new ProcessResult(
            1,
            "==> Linting ./chart\n[ERROR] templates/: parse error at (chart/templates/deployment.yaml:14): unexpected EOF\n",
            "Error: 1 chart(s) linted, 1 chart(s) failed\n",
            TimeSpan.FromMilliseconds(50),
            false,
            false);

        var result = await harness.Client.LintAsync(harness.ChartPath, []);

        Assert.False(result.Success);
        Assert.Equal(1, result.ExitCode);
        var error = Assert.Single(result.Messages);
        Assert.Equal(HelmLintSeverity.Error, error.Severity);
        Assert.Equal("templates/", error.File);
    }

    [Fact]
    public async Task ResolveAsync_reports_the_located_binary_and_version()
    {
        using var harness = HelmTestHarness.Create();

        var executable = await harness.Client.ResolveAsync();

        Assert.True(executable.IsAvailable);
        Assert.Equal(harness.HelmExecutablePath, executable.Path);
        Assert.Equal("4.2.4", executable.Version);
        Assert.Equal(HelmLocator.SourceConfiguration, executable.ResolutionSource);
    }
}
