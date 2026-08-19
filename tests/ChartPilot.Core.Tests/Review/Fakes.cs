using ChartPilot.Core.Charts;
using ChartPilot.Core.Checks;
using ChartPilot.Core.Helm;
using ChartPilot.Core.Manifests;
using ChartPilot.Core.Profiles;
using ChartPilot.Core.Scoring;
using ChartPilot.Core.Values;

namespace ChartPilot.Core.Tests.Review;

/// <summary>
/// Hand-written doubles for the pipeline's collaborators. The pipeline's job is orchestration —
/// layering, resolution, error surfacing — so the tests assert what it passes on, not what the
/// rules decide.
/// </summary>
internal sealed class FakeHelmClient : IHelmClient
{
    public HelmExecutable Executable { get; set; } =
        new(true, "/usr/bin/helm", "v4.2.4", null, "path");

    public HelmTemplateResult TemplateResult { get; set; } =
        new(true, string.Empty, string.Empty, 0, TimeSpan.Zero, false, false);

    public HelmLintResult LintResult { get; set; } =
        new(true, [], string.Empty, 0);

    public HelmTemplateRequest? LastTemplateRequest { get; private set; }

    public int LintCallCount { get; private set; }

    public Task<HelmExecutable> ResolveAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(Executable);

    public Task<HelmTemplateResult> TemplateAsync(HelmTemplateRequest request, CancellationToken cancellationToken = default)
    {
        LastTemplateRequest = request;
        return Task.FromResult(TemplateResult);
    }

    public Task<HelmLintResult> LintAsync(string chartPath, IReadOnlyList<string> valuesFiles, CancellationToken cancellationToken = default)
    {
        LintCallCount++;
        return Task.FromResult(LintResult);
    }
}

internal sealed class FakeManifestParser : IManifestParser
{
    public FakeManifestParser(IReadOnlyList<RenderedResource> resources) => Resources = resources;

    public IReadOnlyList<RenderedResource> Resources { get; set; }

    public string? LastInput { get; private set; }

    public IReadOnlyList<RenderedResource> Parse(string manifests)
    {
        LastInput = manifests;
        return Resources;
    }
}

internal sealed class FakeResourceGraphBuilder : IResourceGraphBuilder
{
    public IResourceGraph Build(IReadOnlyList<RenderedResource> resources) => new FakeResourceGraph(resources);
}

internal sealed class FakeResourceGraph : IResourceGraph
{
    public FakeResourceGraph(IReadOnlyList<RenderedResource> resources) => Resources = resources;

    public IReadOnlyList<RenderedResource> Resources { get; }

    public IReadOnlyList<GraphEdge> Edges => [];

    public IEnumerable<RenderedResource> ByKind(string kind)
        => Resources.Where(r => string.Equals(r.Kind, kind, StringComparison.Ordinal));

    public IEnumerable<RenderedResource> ByKinds(params string[] kinds)
        => Resources.Where(r => kinds.Contains(r.Kind, StringComparer.Ordinal));

    public bool ContainsKind(string kind) => ByKind(kind).Any();

    public RenderedResource? Find(string kind, string name)
        => Resources.FirstOrDefault(r =>
            string.Equals(r.Kind, kind, StringComparison.Ordinal) &&
            string.Equals(r.Name, name, StringComparison.Ordinal));

    public RenderedResource? Resolve(ResourceRef reference) => Find(reference.Kind, reference.Name);

    public IEnumerable<RenderedResource> Workloads()
        => Resources.Where(r => ResourceCategorizer.Categorize(r.Kind) == ResourceCategory.Workloads);

    public IReadOnlyDictionary<string, string> PodLabelsOf(RenderedResource workload)
        => new Dictionary<string, string>(StringComparer.Ordinal);

    public IEnumerable<RenderedResource> SelectorMatches(string kind, IReadOnlyDictionary<string, string> podLabels) => [];

    public IEnumerable<RenderedResource> WorkloadsMatchedBySelector(IReadOnlyDictionary<string, string> selector) => [];

    public IReadOnlyList<GraphEdge> EdgesFrom(ResourceRef source) => [];

    public IReadOnlyList<GraphEdge> EdgesTo(ResourceRef target) => [];
}

internal sealed class FakeChartLoader : IChartLoader
{
    public FakeChartLoader(ChartModel model) => Model = model;

    public ChartModel Model { get; set; }

    public bool ChartDirectory { get; set; } = true;

    public ChartModel Load(string chartDirectory) => Model with { ChartPath = chartDirectory };

    public bool IsChartDirectory(string path) => ChartDirectory;
}

/// <summary>Last layer wins, key by key at the top level — enough to prove the layering order.</summary>
internal sealed class FakeValuesMerger : IValuesMerger
{
    public IReadOnlyList<string> LastLayerNames { get; private set; } = [];

    public ValuesDocument Merge(IReadOnlyList<ValuesDocument> layers, string resultSourceName)
    {
        LastLayerNames = [.. layers.Select(l => l.SourceName)];

        var merged = new Dictionary<string, string?>(StringComparer.Ordinal);

        foreach (var layer in layers)
        {
            foreach (var (path, value) in layer.Flatten())
            {
                merged[path] = value;
            }
        }

        var yaml = string.Join('\n', merged
            .Where(pair => !pair.Key.Contains('.', StringComparison.Ordinal) &&
                           !pair.Key.Contains('[', StringComparison.Ordinal))
            .Select(pair => $"{pair.Key}: {pair.Value ?? "null"}"));

        return ValuesDocument.Parse(yaml, resultSourceName);
    }
}

internal sealed class FakePlatformMetadataReader : IPlatformMetadataReader
{
    public DataClassification Classification { get; set; } = DataClassification.Internal;

    public Exposure Exposure { get; set; } = Exposure.Internal;

    public ValuesDocument? LastValues { get; private set; }

    public DataClassification ReadClassification(ValuesDocument values)
    {
        LastValues = values;
        return Classification;
    }

    public Exposure ReadExposure(ValuesDocument values) => Exposure;
}

internal sealed class FakeProfileStore : IProfileStore
{
    public FakeProfileStore(params Profile[] profiles)
    {
        Profiles = profiles.Length > 0 ? profiles : [MakeProfile("default")];
        Default = Profiles[0];
    }

    public IReadOnlyList<Profile> Profiles { get; }

    public Profile Default { get; }

    public Profile? Get(string id)
        => Profiles.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

    public static Profile MakeProfile(string id) => new(
        id,
        id,
        $"The {id} profile.",
        new ProfileRequirements(),
        new Dictionary<string, Severity>(StringComparer.Ordinal),
        [],
        new ScoreWeights(),
        new SeverityDeductions());
}

internal sealed class FakeSuppressionLoader : ISuppressionLoader
{
    public IReadOnlyList<Suppression> Suppressions { get; set; } = [];

    public string? LastDirectory { get; private set; }

    public IReadOnlyList<Suppression> Load(string chartDirectory)
    {
        LastDirectory = chartDirectory;
        return Suppressions;
    }
}

internal sealed class FakeCheckEngine : ICheckEngine
{
    public CheckRunResult Result { get; set; } = new([], [], []);

    public CheckContext? LastContext { get; private set; }

    public IReadOnlyList<Suppression> LastSuppressions { get; private set; } = [];

    public CheckRunResult Run(CheckContext context, IReadOnlyList<Suppression> suppressions)
    {
        LastContext = context;
        LastSuppressions = suppressions;
        return Result;
    }
}

internal sealed class FakeScorer : IScorer
{
    public ScoreReport Report { get; set; } = new(100, []);

    public Profile? LastProfile { get; private set; }

    public ScoreReport Score(IReadOnlyList<Finding> findings, IReadOnlyList<PassedCheck> passed, Profile profile)
    {
        LastProfile = profile;
        return Report;
    }
}

internal sealed class StubTimeProvider : TimeProvider
{
    private readonly DateTimeOffset _now;

    public StubTimeProvider(DateTimeOffset now) => _now = now;

    public override DateTimeOffset GetUtcNow() => _now;
}
