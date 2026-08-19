using ChartPilot.Core.Helm;
using ChartPilot.Core.Io;
using ChartPilot.Core.Review;
using ChartPilot.Core.Tests.Reporting;

namespace ChartPilot.Core.Tests.Review;

public sealed class RenderServiceTests
{
    private readonly FakeHelmClient _helm = new();
    private readonly FakeManifestParser _parser = new([
        ReviewResultFactory.Resource("apps/v1", "Deployment", "member-api", "templates/deployment.yaml")
    ]);

    private RenderService CreateService() => new(_helm, _parser, new FakeResourceGraphBuilder(), new PhysicalFileSystem());

    private static ReviewRequest Request(string chartPath, params string[] valuesFiles)
        => new(chartPath, "member-api", valuesFiles, null, "default", "test");

    [Fact]
    public async Task RenderAsync_parses_the_manifest_stream_and_builds_a_graph()
    {
        using var chart = new TempChart();
        _helm.TemplateResult = _helm.TemplateResult with { Manifests = "kind: Deployment\n" };

        var outcome = await CreateService().RenderAsync(Request(chart.Path));

        Assert.True(outcome.Success);
        Assert.Null(outcome.Error);
        Assert.Equal("kind: Deployment\n", _parser.LastInput);
        Assert.Single(outcome.Resources);
        Assert.NotNull(outcome.Graph);
        Assert.Equal(outcome.Resources, outcome.Graph!.Resources);
    }

    [Fact]
    public async Task RenderAsync_resolves_values_file_names_against_the_chart_directory()
    {
        using var chart = new TempChart();
        var prod = chart.Write("values-prod.yaml", "replicaCount: 3\n");

        await CreateService().RenderAsync(Request(chart.Path, "values-prod.yaml"));

        Assert.NotNull(_helm.LastTemplateRequest);
        Assert.Equal(new[] { prod }, _helm.LastTemplateRequest!.ValuesFiles);
    }

    [Fact]
    public async Task RenderAsync_renders_with_crds_included_and_tests_skipped()
    {
        using var chart = new TempChart();

        await CreateService().RenderAsync(Request(chart.Path));

        Assert.True(_helm.LastTemplateRequest!.IncludeCrds);
        Assert.True(_helm.LastTemplateRequest.SkipTests);
        Assert.False(_helm.LastTemplateRequest.DependencyUpdate);
    }

    [Fact]
    public async Task RenderAsync_surfaces_helm_stderr_rather_than_an_empty_result()
    {
        using var chart = new TempChart();
        _helm.TemplateResult = new HelmTemplateResult(
            false,
            string.Empty,
            "Error: template: chart/templates/deployment.yaml:12:14: nil pointer",
            1,
            TimeSpan.Zero,
            false,
            false);

        var outcome = await CreateService().RenderAsync(Request(chart.Path));

        Assert.False(outcome.Success);
        Assert.Null(outcome.Graph);
        Assert.Empty(outcome.Resources);
        Assert.Contains("exit code 1", outcome.Error!, StringComparison.Ordinal);
        Assert.Contains("nil pointer", outcome.HelmStdErr!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RenderAsync_reports_a_timeout_as_such()
    {
        using var chart = new TempChart();
        _helm.TemplateResult = new HelmTemplateResult(
            false, string.Empty, string.Empty, -1, TimeSpan.FromSeconds(30), true, false);

        var outcome = await CreateService().RenderAsync(Request(chart.Path));

        Assert.False(outcome.Success);
        Assert.Equal("helm template timed out.", outcome.Error);
    }

    [Fact]
    public async Task RenderAsync_rejects_a_missing_chart_directory()
    {
        var service = CreateService();
        var missing = Path.Combine(Path.GetTempPath(), "chartpilot-tests", Guid.NewGuid().ToString("n"));

        var exception = await Assert.ThrowsAsync<ReviewException>(
            () => service.RenderAsync(Request(missing)));

        Assert.Contains("Chart directory not found", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RenderAsync_rejects_a_values_file_that_does_not_exist()
    {
        using var chart = new TempChart();
        var service = CreateService();

        var exception = await Assert.ThrowsAsync<ReviewException>(
            () => service.RenderAsync(Request(chart.Path, "values-nope.yaml")));

        Assert.Contains("values-nope.yaml", exception.Message, StringComparison.Ordinal);
    }
}
