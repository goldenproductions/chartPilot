using ChartPilot.Core.Checks;
using ChartPilot.Core.Manifests;
using ChartPilot.Core.Profiles;
using ChartPilot.Core.Scoring;
using ChartPilot.Core.Tests.Checks;

namespace ChartPilot.Core.Tests.Scoring;

public class ScorerTests
{
    private static readonly IScorer DefaultScorer = new Scorer();

    private static Finding MakeFinding(string checkId, Severity severity)
        => new(checkId, severity, new ResourceRef("Deployment", "member-api"), "message", "remediation");

    [Fact]
    public void AllFourCategoriesAlwaysAppear()
    {
        var report = DefaultScorer.Score([], [], BuiltInProfiles.Default);

        Assert.Equal(4, report.Categories.Count);
        Assert.NotNull(report[CheckCategory.Security]);
        Assert.NotNull(report[CheckCategory.Reliability]);
        Assert.NotNull(report[CheckCategory.Operability]);
        Assert.NotNull(report[CheckCategory.Governance]);
    }

    [Fact]
    public void NoFindingsScoresOneHundred()
    {
        var report = DefaultScorer.Score([], [], BuiltInProfiles.Default);

        Assert.Equal(100, report.Overall);
        Assert.All(report.Categories, c => Assert.Equal(100, c.Score));
    }

    [Theory]
    // one critical in one category: 100 - 25 = 75, weighted 0.35*75 + 65 = 91.25 -> 91
    [InlineData("CP-SEC-001", Severity.Critical, 1, 75, 91)]
    // three warnings: 100 - 24 = 76, weighted 0.35*76 + 65 = 91.6 -> 92
    [InlineData("CP-SEC-001", Severity.Warning, 3, 76, 92)]
    // info deducts nothing by default
    [InlineData("CP-SEC-001", Severity.Info, 9, 100, 100)]
    // the clamp at zero: five criticals would deduct 125
    [InlineData("CP-SEC-001", Severity.Critical, 5, 0, 65)]
    public void SecurityFindingsDeductAsSpecified(
        string checkId, Severity severity, int count, int expectedCategory, int expectedOverall)
    {
        var findings = Enumerable.Range(0, count).Select(_ => MakeFinding(checkId, severity)).ToArray();

        var report = DefaultScorer.Score(findings, [], BuiltInProfiles.Default);

        Assert.Equal(expectedCategory, report[CheckCategory.Security]!.Score);
        Assert.Equal(expectedOverall, report.Overall);
    }

    [Fact]
    public void EachCategoryIsScoredIndependentlyAndWeighted()
    {
        Finding[] findings =
        [
            MakeFinding("CP-SEC-001", Severity.Critical),   // Security  100 - 25 = 75
            MakeFinding("CP-REL-001", Severity.Warning),    // Reliability 100 - 8 = 92
            MakeFinding("CP-CERT-002", Severity.Warning),   // Operability 100 - 8 = 92
            MakeFinding("CP-GOV-001", Severity.Critical)    // Governance 100 - 25 = 75
        ];

        var report = DefaultScorer.Score(findings, [], BuiltInProfiles.Default);

        Assert.Equal(75, report[CheckCategory.Security]!.Score);
        Assert.Equal(92, report[CheckCategory.Reliability]!.Score);
        Assert.Equal(92, report[CheckCategory.Operability]!.Score);
        Assert.Equal(75, report[CheckCategory.Governance]!.Score);

        // 0.35*75 + 0.30*92 + 0.20*92 + 0.15*75 = 26.25 + 27.6 + 18.4 + 11.25 = 83.5 -> 84
        Assert.Equal(84, report.Overall);
    }

    [Fact]
    public void EveryCountOnCategoryScoreIsPopulated()
    {
        Finding[] findings =
        [
            MakeFinding("CP-SEC-001", Severity.Critical),
            MakeFinding("CP-SEC-003", Severity.Warning),
            MakeFinding("CP-SEC-004", Severity.Warning),
            MakeFinding("CP-SEC-006", Severity.Info)
        ];

        PassedCheck[] passed =
        [
            new("CP-SEC-002", "Container runs privileged", CheckCategory.Security),
            new("CP-REL-001", "Container has no readinessProbe", CheckCategory.Reliability)
        ];

        var report = DefaultScorer.Score(findings, passed, BuiltInProfiles.Default);
        var security = report[CheckCategory.Security]!;

        Assert.Equal(1, security.CriticalCount);
        Assert.Equal(2, security.WarningCount);
        Assert.Equal(1, security.InfoCount);
        Assert.Equal(1, security.PassedCount);
        Assert.Equal(1, report[CheckCategory.Reliability]!.PassedCount);
    }

    [Fact]
    public void TheProfileOwnsTheDeductionsAndTheWeights()
    {
        var profile = BuiltInProfiles.Default with
        {
            Deductions = new SeverityDeductions(Critical: 50, Warning: 1, Info: 1),
            Weights = new ScoreWeights(Security: 1.0, Reliability: 0, Operability: 0, Governance: 0)
        };

        var report = DefaultScorer.Score([MakeFinding("CP-SEC-001", Severity.Critical)], [], profile);

        Assert.Equal(50, report[CheckCategory.Security]!.Score);
        Assert.Equal(50, report.Overall);
    }

    [Fact]
    public void TheCatalogDecidesTheCategoryOfAFinding()
    {
        // CP-NET-005 is declared as a Reliability rule even though the prefix is a network one.
        var scorer = new Scorer(CheckTestHarness.Catalog);

        var report = scorer.Score([MakeFinding("CP-NET-005", Severity.Critical)], [], BuiltInProfiles.Default);

        Assert.Equal(75, report[CheckCategory.Reliability]!.Score);
        Assert.Equal(100, report[CheckCategory.Security]!.Score);
    }

    [Fact]
    public void SandboxDeductionsMoveTheScoreLessThanTheDefaultProfile()
    {
        Finding[] findings = [MakeFinding("CP-SEC-001", Severity.Critical), MakeFinding("CP-SEC-002", Severity.Critical)];

        var strict = DefaultScorer.Score(findings, [], BuiltInProfiles.Default);
        var sandbox = DefaultScorer.Score(findings, [], BuiltInProfiles.SandboxService);

        Assert.Equal(50, strict[CheckCategory.Security]!.Score);
        Assert.Equal(76, sandbox[CheckCategory.Security]!.Score);
    }
}
