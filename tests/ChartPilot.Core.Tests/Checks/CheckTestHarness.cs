using ChartPilot.Core.Charts;
using ChartPilot.Core.Checks;
using ChartPilot.Core.Helm;
using ChartPilot.Core.Manifests;
using ChartPilot.Core.Profiles;
using ChartPilot.Core.Values;

namespace ChartPilot.Core.Tests.Checks;

/// <summary>
/// Runs the real catalog through the real engine over a fixture graph.
/// <para>
/// Tests go through the engine rather than instantiating a rule directly, which is deliberate: the
/// engine is what resolves the severity, so an assertion on the resolved severity is only
/// meaningful when the engine produced it.
/// </para>
/// </summary>
public static class CheckTestHarness
{
    /// <summary>The catalog is stateless, so one instance serves the whole test run.</summary>
    public static readonly CheckCatalog Catalog = CheckCatalog.CreateDefault();

    /// <summary>A fixed "today" so suppression expiry tests never depend on the wall clock.</summary>
    public static readonly DateTimeOffset Today = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    public static CheckEngine CreateEngine(DateTimeOffset? now = null)
        => new(Catalog, new SeverityResolver(), new FixedTimeProvider(now ?? Today));

    public static CheckContext Context(
        IResourceGraph graph,
        Profile? profile = null,
        DataClassification classification = DataClassification.Unclassified,
        string valuesYaml = "{}",
        ChartModel? chart = null,
        IReadOnlyList<HelmLintMessage>? lintMessages = null,
        string environment = "test",
        Exposure exposure = Exposure.Unknown,
        bool? lintRan = null)
        => new(
            graph,
            ValuesDocument.Parse(valuesYaml, "values.yaml"),
            profile ?? BuiltInProfiles.Default,
            classification,
            environment)
        {
            Chart = chart,
            Exposure = exposure,
            LintMessages = lintMessages ?? [],
            // Passing messages means lint ran; a test that wants "lint ran and was clean" says so.
            LintRan = lintRan ?? lintMessages is not null
        };

    /// <summary>Runs the whole catalog and returns everything it produced.</summary>
    public static CheckRunResult Run(
        CheckContext context,
        IReadOnlyList<Suppression>? suppressions = null,
        DateTimeOffset? now = null)
        => CreateEngine(now).Run(context, suppressions ?? []);

    /// <summary>Runs the catalog over a fixture and returns only the findings of one rule.</summary>
    public static IReadOnlyList<Finding> FindingsFor(string checkId, string fixture, Profile? profile = null,
        DataClassification classification = DataClassification.Unclassified)
    {
        var context = Context(TestGraph.FromFixture(fixture), profile, classification);
        return Run(context).Findings.Where(f => f.CheckId == checkId).ToArray();
    }

    /// <summary>True when the rule ran, found nothing and was therefore reported as passed.</summary>
    public static bool Passed(string checkId, string fixture, Profile? profile = null,
        DataClassification classification = DataClassification.Unclassified)
    {
        var context = Context(TestGraph.FromFixture(fixture), profile, classification);
        return Run(context).Passed.Any(p => p.CheckId == checkId);
    }

    /// <summary>A deterministic clock, so "expired" means expired relative to the test, not to today.</summary>
    public sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
