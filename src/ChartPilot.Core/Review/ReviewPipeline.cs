using ChartPilot.Core.Charts;
using ChartPilot.Core.Checks;
using ChartPilot.Core.Helm;
using ChartPilot.Core.Io;
using ChartPilot.Core.Profiles;
using ChartPilot.Core.Scoring;
using ChartPilot.Core.Values;

namespace ChartPilot.Core.Review;

/// <summary>
/// The one pipeline the GUI, the CLI and the report writer all enter: chart metadata, effective
/// values, render, classification, profile, suppressions, lint, checks, score.
/// </summary>
public sealed class ReviewPipeline : IReviewPipeline
{
    private readonly IChartLoader _chartLoader;
    private readonly IValuesMerger _merger;
    private readonly IRenderService _renderService;
    private readonly IPlatformMetadataReader _metadataReader;
    private readonly IProfileStore _profiles;
    private readonly ISuppressionLoader _suppressions;
    private readonly ICheckEngine _engine;
    private readonly IScorer _scorer;
    private readonly IHelmClient _helm;
    private readonly IFileSystem _fileSystem;
    private readonly ValuesFileResolver _valuesFiles;
    private readonly TimeProvider _time;

    public ReviewPipeline(
        IChartLoader chartLoader,
        IValuesMerger merger,
        IRenderService renderService,
        IPlatformMetadataReader metadataReader,
        IProfileStore profiles,
        ISuppressionLoader suppressions,
        ICheckEngine engine,
        IScorer scorer,
        IHelmClient helm,
        IFileSystem fileSystem,
        TimeProvider time)
    {
        _chartLoader = chartLoader;
        _merger = merger;
        _renderService = renderService;
        _metadataReader = metadataReader;
        _profiles = profiles;
        _suppressions = suppressions;
        _engine = engine;
        _scorer = scorer;
        _helm = helm;
        _fileSystem = fileSystem;
        _valuesFiles = new ValuesFileResolver(fileSystem);
        _time = time;
    }

    public async Task<ReviewResult> ReviewAsync(ReviewRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var chartDirectory = _valuesFiles.NormalizeChartDirectory(request.ChartPath);

        if (!_chartLoader.IsChartDirectory(chartDirectory))
        {
            throw new ReviewException($"No Chart.yaml found in: {request.ChartPath}");
        }

        ChartModel chart;
        ValuesDocument values;

        try
        {
            chart = _chartLoader.Load(chartDirectory);
            values = BuildEffectiveValues(chartDirectory, request);
        }
        catch (ChartLoadException ex)
        {
            // A malformed Chart.yaml is user input, not a tool fault: it has to reach the caller as
            // the documented review failure, not as an unhandled exception.
            throw new ReviewException(ex.Message);
        }
        catch (ValuesParseException ex)
        {
            throw new ReviewException(ex.Message);
        }

        var render = await _renderService.RenderAsync(request, ct).ConfigureAwait(false);

        if (!render.Success || render.Graph is null)
        {
            throw new ReviewException(
                render.Error ?? "helm template failed.",
                render.HelmStdErr,
                HelmErrorSource.Extract(render.HelmStdErr));
        }

        var classification = _metadataReader.ReadClassification(values);
        var exposure = _metadataReader.ReadExposure(values);
        var profile = _profiles.Get(request.ProfileId) ?? _profiles.Default;
        var suppressions = _suppressions.Load(chartDirectory);
        var lint = await RunLintAsync(chartDirectory, request, ct).ConfigureAwait(false);

        var context = new CheckContext(
            render.Graph,
            values,
            profile,
            classification,
            string.IsNullOrWhiteSpace(request.Environment) ? "default" : request.Environment)
        {
            Chart = chart,
            Exposure = exposure,
            LintMessages = lint.Messages,
            LintRan = lint.Ran
        };

        var run = _engine.Run(context, suppressions);
        var score = _scorer.Score(run.Findings, run.Passed, profile);
        var helmVersion = await ResolveHelmVersionAsync(ct).ConfigureAwait(false);

        return new ReviewResult(
            chart,
            context.Environment,
            profile.Id,
            classification,
            render.Resources,
            run.Findings,
            run.Passed,
            run.Suppressed,
            score,
            helmVersion,
            _time.GetUtcNow());
    }

    /// <summary>
    /// The chart's own values.yaml first, then the selected files in order, then the editor draft —
    /// exactly the layering <c>helm template -f a -f b</c> applies, so the checks see what Helm saw.
    /// </summary>
    private ValuesDocument BuildEffectiveValues(string chartDirectory, ReviewRequest request)
    {
        var layers = new List<ValuesDocument>();
        var selected = _valuesFiles.Resolve(chartDirectory, request.ValuesFiles);
        var defaultValues = _valuesFiles.DefaultValuesFile(chartDirectory);

        if (defaultValues is not null &&
            !selected.Contains(defaultValues, StringComparer.OrdinalIgnoreCase))
        {
            layers.Add(Read(defaultValues));
        }

        foreach (var file in selected)
        {
            layers.Add(Read(file));
        }

        if (!string.IsNullOrWhiteSpace(request.DraftValuesYaml))
        {
            layers.Add(ValuesDocument.Parse(request.DraftValuesYaml, "draft"));
        }

        if (layers.Count == 0)
        {
            return ValuesDocument.Empty("effective");
        }

        return _merger.Merge(layers, "effective");
    }

    private ValuesDocument Read(string path)
    {
        try
        {
            return ValuesDocument.Parse(_fileSystem.ReadAllText(path), Path.GetFileName(path));
        }
        catch (ValuesParseException ex)
        {
            throw new ReviewException($"{Path.GetFileName(path)}: {ex.Message}", null, Path.GetFileName(path));
        }
        catch (IOException ex)
        {
            throw new ReviewException($"Could not read {path}: {ex.Message}");
        }
    }

    /// <summary>
    /// Runs <c>helm lint</c> and reports whether it ran at all. A chart that lints clean produces no
    /// messages, so the CP-GOV-006/007/008 rules need the flag to tell "clean" from "never ran" — and
    /// without it a clean chart never appears in the passed list.
    /// </summary>
    private async Task<LintOutcome> RunLintAsync(
        string chartDirectory,
        ReviewRequest request,
        CancellationToken ct)
    {
        if (!request.RunLint)
        {
            return LintOutcome.NotRun;
        }

        var valuesFiles = _valuesFiles.Resolve(chartDirectory, request.ValuesFiles);

        try
        {
            var lint = await _helm.LintAsync(chartDirectory, valuesFiles, ct).ConfigureAwait(false);
            return new LintOutcome(true, lint.Messages);
        }
        catch (HelmNotAvailableException)
        {
            // The render already succeeded, so helm exists; a lint that cannot run is not worth
            // failing a review over.
            return LintOutcome.NotRun;
        }
    }

    private readonly record struct LintOutcome(bool Ran, IReadOnlyList<HelmLintMessage> Messages)
    {
        public static LintOutcome NotRun { get; } = new(false, []);
    }

    private async Task<string?> ResolveHelmVersionAsync(CancellationToken ct)
    {
        try
        {
            var executable = await _helm.ResolveAsync(ct).ConfigureAwait(false);
            return executable.Version;
        }
        catch (HelmNotAvailableException)
        {
            return null;
        }
    }
}
