using ChartPilot.Core.Charts;
using ChartPilot.Core.Checks;
using ChartPilot.Core.Manifests;
using ChartPilot.Core.Profiles;
using ChartPilot.Core.Reporting;
using ChartPilot.Core.Review;
using ChartPilot.Core.Scoring;
using ChartPilot.Core.Values;
using Microsoft.Extensions.DependencyInjection;

namespace ChartPilot.Helm.Tests;

/// <summary>
/// The end-to-end row of the testing strategy: the real pipeline, the real helm binary and the
/// deliberately-bad chart in <c>samples/charts/</c>, asserted down to the score and the exact set of
/// critical rules. Every other test in the suite substitutes something; this one substitutes nothing,
/// so it is the only test that fails when two rules interact badly, when the profile wiring breaks,
/// or when scoring drifts.
/// </summary>
public sealed class EndToEndSampleChartTests
{
    /// <summary>The bad chart's critical rules. A new critical rule has to be added here on purpose.</summary>
    private static readonly string[] ExpectedCriticalIds =
    [
        "CP-NET-002", // public route with no AuthorizationPolicy
        "CP-NET-003", // no STRICT mTLS
        "CP-REL-004", // no resource limits
        "CP-SEC-001", // runs as root
        "CP-SEC-002", // privileged
        "CP-SEC-005", // :latest
        "CP-SEC-007", // inline secret material
        "CP-SEC-008", // no NetworkPolicy
        "CP-SEC-010", // wildcard RBAC
        "CP-SEC-014"  // public route on a sensitive-personal-data service
    ];

    [Fact]
    public async Task TheDeliberatelyBadChart_ProducesTheKnownScoreAndFindings()
    {
        var result = await ReviewAsync("insecure-member-api");

        Assert.Equal(15, result.Score.Overall);
        Assert.Equal(0, CategoryScore(result, CheckCategory.Security));
        Assert.Equal(11, CategoryScore(result, CheckCategory.Reliability));
        Assert.Equal(12, CategoryScore(result, CheckCategory.Operability));
        Assert.Equal(60, CategoryScore(result, CheckCategory.Governance));

        var criticalIds = result.Findings
            .Where(f => f.Severity == Severity.Critical)
            .Select(f => f.CheckId)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(ExpectedCriticalIds, criticalIds);

        // The chart declares sensitive-personal-data, which is what promotes the network boundary,
        // the mesh encryption and the public route to Critical.
        Assert.Equal(DataClassification.SensitivePersonalData, result.Classification);

        // Nine rendered resources, and every finding points at one of them or at the chart itself.
        Assert.Equal(9, result.Resources.Count);
        Assert.All(
            result.Findings.Where(f => f.Resource is not null),
            f => Assert.Contains(result.Resources, r => r.Ref.Key == f.Resource!.Key));
    }

    [Fact]
    public async Task TheDeliberatelyBadChart_ReportsNoDuplicateFindings()
    {
        var result = await ReviewAsync("insecure-member-api");

        var duplicates = result.Findings
            .GroupBy(f => string.Join('', f.CheckId, f.Resource?.Key ?? string.Empty, f.Message),
                StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToArray();

        Assert.Empty(duplicates);
    }

    [Fact]
    public async Task TheGoldenPathChart_PassesEveryApplicableRule()
    {
        var result = await ReviewAsync("member-api");

        Assert.Equal(100, result.Score.Overall);
        Assert.DoesNotContain(result.Findings, f => f.Severity is Severity.Critical or Severity.Warning);

        // The reference chart is what proves the catalog is satisfiable at all: most of it has to be
        // evaluated and pass, not merely be inapplicable.
        Assert.True(result.Passed.Count >= 40, $"Only {result.Passed.Count} rules were evaluated and passed.");
    }

    [Fact]
    public async Task TheReport_IsWrittenForTheBadChart()
    {
        using var provider = BuildProvider();

        var result = await provider.GetRequiredService<IReviewPipeline>()
            .ReviewAsync(RequestFor("insecure-member-api"));

        var markdown = provider.GetRequiredService<IReportWriter>().Write(result);

        Assert.Contains("# ChartPilot Review: insecure-member-api", markdown, StringComparison.Ordinal);
        Assert.Contains("Overall score: **15/100**", markdown, StringComparison.Ordinal);
        Assert.Contains("CP-SEC-002", markdown, StringComparison.Ordinal);
    }

    /// <summary>A values file the chart does not ship is a review failure, not a silently skipped layer.</summary>
    [Fact]
    public async Task AnUnknownValuesFile_FailsTheReview()
    {
        using var provider = BuildProvider();
        var request = RequestFor("member-api") with { ValuesFiles = ["values-nope.yaml"] };

        var exception = await Assert.ThrowsAsync<ReviewException>(
            () => provider.GetRequiredService<IReviewPipeline>().ReviewAsync(request));

        Assert.Contains("values-nope.yaml", exception.Message, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ harness

    private static int CategoryScore(ReviewResult result, CheckCategory category)
        => result.Score.Categories.Single(c => c.Category == category).Score;

    private static async Task<ReviewResult> ReviewAsync(string chartName)
    {
        using var provider = BuildProvider();
        return await provider.GetRequiredService<IReviewPipeline>().ReviewAsync(RequestFor(chartName));
    }

    private static ReviewRequest RequestFor(string chartName)
        => new(
            Path.Combine(SamplesRoot, chartName),
            chartName,
            [],
            null,
            "default",
            "default");

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();

        services.AddLogging();
        services
            .AddChartPilotCharts()
            .AddChartPilotValues()
            .AddChartPilotManifests()
            .AddChartPilotChecks()
            .AddChartPilotProfiles()
            .AddChartPilotScoring()
            .AddChartPilotReview()
            .AddChartPilotReporting()
            .AddChartPilotHelm(options => options.AllowlistRoot = RepositoryRoot);

        return services.BuildServiceProvider();
    }

    private static string SamplesRoot => Path.Combine(RepositoryRoot, "samples", "charts");

    /// <summary>Walks up from the test binaries to the checkout that contains the solution file.</summary>
    private static string RepositoryRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);

            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ChartPilot.sln")))
            {
                directory = directory.Parent;
            }

            return directory?.FullName
                   ?? throw new InvalidOperationException("ChartPilot.sln was not found above the test output directory.");
        }
    }
}
