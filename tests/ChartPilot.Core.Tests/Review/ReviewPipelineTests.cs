using ChartPilot.Core.Checks;
using ChartPilot.Core.Helm;
using ChartPilot.Core.Io;
using ChartPilot.Core.Profiles;
using ChartPilot.Core.Review;
using ChartPilot.Core.Scoring;
using ChartPilot.Core.Tests.Reporting;

namespace ChartPilot.Core.Tests.Review;

public sealed class ReviewPipelineTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 19, 10, 15, 0, TimeSpan.Zero);

    private readonly FakeChartLoader _chartLoader = new(ReviewResultFactory.SampleChart());
    private readonly FakeValuesMerger _merger = new();
    private readonly FakePlatformMetadataReader _metadata = new();
    private readonly FakeProfileStore _profiles = new(
        FakeProfileStore.MakeProfile("default"),
        FakeProfileStore.MakeProfile("sensitive-member-data-service"));
    private readonly FakeSuppressionLoader _suppressions = new();
    private readonly FakeCheckEngine _engine = new();
    private readonly FakeScorer _scorer = new();
    private readonly FakeHelmClient _helm = new();
    private readonly StubRenderService _render = new();

    private ReviewPipeline CreatePipeline() => new(
        _chartLoader,
        _merger,
        _render,
        _metadata,
        _profiles,
        _suppressions,
        _engine,
        _scorer,
        _helm,
        new PhysicalFileSystem(),
        new StubTimeProvider(Now));

    private static ReviewRequest Request(
        string chartPath,
        string profileId = "default",
        string? draft = null,
        bool runLint = true,
        string[]? valuesFiles = null)
        => new(chartPath, "member-api", valuesFiles ?? [], draft, profileId, "prod", RunLint: runLint);

    [Fact]
    public async Task ReviewAsync_layers_the_default_values_then_the_selection_then_the_draft()
    {
        using var chart = new TempChart();
        chart.Write("values.yaml", "replicaCount: 1\n");
        chart.Write("values-prod.yaml", "replicaCount: 3\n");

        await CreatePipeline().ReviewAsync(
            Request(chart.Path, draft: "replicaCount: 5\n", valuesFiles: ["values-prod.yaml"]));

        Assert.Equal(
            new[] { "values.yaml", "values-prod.yaml", "draft" },
            _merger.LastLayerNames);
    }

    [Fact]
    public async Task ReviewAsync_does_not_layer_the_default_values_twice()
    {
        using var chart = new TempChart();
        chart.Write("values.yaml", "replicaCount: 1\n");

        await CreatePipeline().ReviewAsync(Request(chart.Path, valuesFiles: ["values.yaml"]));

        Assert.Equal(new[] { "values.yaml" }, _merger.LastLayerNames);
    }

    [Fact]
    public async Task ReviewAsync_falls_back_to_the_default_profile_for_an_unknown_id()
    {
        using var chart = new TempChart();

        var result = await CreatePipeline().ReviewAsync(Request(chart.Path, profileId: "no-such-profile"));

        Assert.Equal("default", result.ProfileId);
        Assert.Equal("default", _scorer.LastProfile!.Id);
    }

    [Fact]
    public async Task ReviewAsync_uses_the_requested_profile_when_it_exists()
    {
        using var chart = new TempChart();

        var result = await CreatePipeline().ReviewAsync(
            Request(chart.Path, profileId: "sensitive-member-data-service"));

        Assert.Equal("sensitive-member-data-service", result.ProfileId);
    }

    [Fact]
    public async Task ReviewAsync_feeds_lint_messages_into_the_check_context()
    {
        using var chart = new TempChart();
        _helm.LintResult = new HelmLintResult(
            true,
            [new HelmLintMessage(HelmLintSeverity.Warning, "templates/deployment.yaml", "icon is recommended")],
            string.Empty,
            0);

        await CreatePipeline().ReviewAsync(Request(chart.Path));

        Assert.Equal(1, _helm.LintCallCount);
        Assert.Single(_engine.LastContext!.LintMessages);
        Assert.Equal("icon is recommended", _engine.LastContext.LintMessages[0].Message);
    }

    [Fact]
    public async Task ReviewAsync_skips_lint_when_the_caller_asked_it_to()
    {
        using var chart = new TempChart();

        await CreatePipeline().ReviewAsync(Request(chart.Path, runLint: false));

        Assert.Equal(0, _helm.LintCallCount);
        Assert.Empty(_engine.LastContext!.LintMessages);
    }

    [Fact]
    public async Task ReviewAsync_assembles_the_result_from_every_stage()
    {
        using var chart = new TempChart();
        _metadata.Classification = DataClassification.SensitivePersonalData;
        _scorer.Report = new ScoreReport(64, [new CategoryScore(CheckCategory.Security, 50, 2, 0, 0, 3)]);
        _engine.Result = new CheckRunResult(
            [new Finding("CP-SEC-001", Severity.Critical, null, "Runs as root.", "Set runAsNonRoot.")],
            [new PassedCheck("CP-REL-001", "Readiness probe configured", CheckCategory.Reliability)],
            []);

        var result = await CreatePipeline().ReviewAsync(Request(chart.Path));

        Assert.Equal("member-api", result.Chart.Name);
        Assert.Equal("prod", result.Environment);
        Assert.Equal(DataClassification.SensitivePersonalData, result.Classification);
        Assert.Equal(64, result.Score.Overall);
        Assert.Equal(1, result.CriticalCount);
        Assert.Single(result.Passed);
        Assert.Equal("v4.2.4", result.HelmVersion);
        Assert.Equal(Now, result.GeneratedAt);
        Assert.Equal(_render.Outcome.Resources, result.Resources);
    }

    [Fact]
    public async Task ReviewAsync_passes_the_chart_model_and_suppressions_to_the_engine()
    {
        using var chart = new TempChart();
        _suppressions.Suppressions = [new Suppression("CP-SEC-004", "Deployment/legacy", "Tracked in PLAT-412", null)];

        await CreatePipeline().ReviewAsync(Request(chart.Path));

        Assert.Equal(Path.GetFullPath(chart.Path), _suppressions.LastDirectory);
        Assert.Single(_engine.LastSuppressions);
        Assert.NotNull(_engine.LastContext!.Chart);
        Assert.Equal("member-api", _engine.LastContext.Chart!.Name);
    }

    [Fact]
    public async Task ReviewAsync_surfaces_a_failed_render_as_a_ReviewException_with_helm_stderr()
    {
        using var chart = new TempChart();
        _render.Outcome = new RenderOutcome(
            false,
            [],
            null,
            string.Empty,
            "helm template failed with exit code 1.",
            "Error: template: chart/templates/deployment.yaml:12:14: nil pointer evaluating interface {}.tag");

        var pipeline = CreatePipeline();

        var exception = await Assert.ThrowsAsync<ReviewException>(
            () => pipeline.ReviewAsync(Request(chart.Path)));

        Assert.Contains("exit code 1", exception.Message, StringComparison.Ordinal);
        Assert.Contains("nil pointer", exception.HelmStdErr!, StringComparison.Ordinal);
        Assert.Equal("chart/templates/deployment.yaml:12:14", exception.FailingSource);
    }

    [Fact]
    public async Task ReviewAsync_rejects_a_directory_without_a_chart()
    {
        using var chart = new TempChart();
        _chartLoader.ChartDirectory = false;

        var pipeline = CreatePipeline();

        var exception = await Assert.ThrowsAsync<ReviewException>(
            () => pipeline.ReviewAsync(Request(chart.Path)));

        Assert.Contains("No Chart.yaml", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReviewAsync_reports_a_values_file_that_is_not_there()
    {
        using var chart = new TempChart();
        var pipeline = CreatePipeline();

        var exception = await Assert.ThrowsAsync<ReviewException>(
            () => pipeline.ReviewAsync(Request(chart.Path, valuesFiles: ["values-missing.yaml"])));

        Assert.Contains("values-missing.yaml", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>Stands in for the render half so the pipeline tests stay about orchestration.</summary>
    private sealed class StubRenderService : IRenderService
    {
        public RenderOutcome Outcome { get; set; } = new(
            true,
            [ReviewResultFactory.Resource("apps/v1", "Deployment", "member-api", "templates/deployment.yaml")],
            new FakeResourceGraph([
                ReviewResultFactory.Resource("apps/v1", "Deployment", "member-api", "templates/deployment.yaml")
            ]),
            "kind: Deployment\n",
            null,
            null);

        public ReviewRequest? LastRequest { get; private set; }

        public Task<RenderOutcome> RenderAsync(ReviewRequest request, CancellationToken ct = default)
        {
            LastRequest = request;
            return Task.FromResult(Outcome);
        }
    }
}
