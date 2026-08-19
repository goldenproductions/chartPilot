using ChartPilot.Core.Checks;
using ChartPilot.Core.Profiles;
using ChartPilot.Core.Scoring;

namespace ChartPilot.Core.Tests.Checks;

/// <summary>
/// The M3 acceptance demo: the same unchanged chart, reviewed under the most permissive and the
/// strictest golden path profile. If switching the profile did not materially move both the findings
/// and the score, profiles would be decoration rather than policy.
/// </summary>
public class ProfileImpactTests
{
    private const string Fixture = "security/workload-bad.yaml";

    private static readonly IScorer Scorer = new Scorer(CheckTestHarness.Catalog);

    private static (CheckRunResult Result, ScoreReport Score) Review(
        Profile profile, DataClassification classification)
    {
        var context = CheckTestHarness.Context(TestGraph.FromFixture(Fixture), profile, classification);
        var result = CheckTestHarness.Run(context);

        return (result, Scorer.Score(result.Findings, result.Passed, profile));
    }

    [Fact]
    public void SwitchingFromSandboxToSensitiveMovesBothTheFindingsAndTheScore()
    {
        var (sandbox, sandboxScore) = Review(BuiltInProfiles.SandboxService, DataClassification.Unclassified);
        var (sensitive, sensitiveScore) = Review(
            BuiltInProfiles.SensitiveMemberDataService, DataClassification.SensitivePersonalData);

        Assert.True(
            sensitive.Findings.Count > sandbox.Findings.Count,
            $"sensitive produced {sensitive.Findings.Count} findings, sandbox produced {sandbox.Findings.Count}.");

        Assert.True(
            sensitiveScore.Overall < sandboxScore.Overall,
            $"sensitive scored {sensitiveScore.Overall}, sandbox scored {sandboxScore.Overall}.");

        Assert.True(
            CountOf(sensitive, Severity.Critical) > CountOf(sandbox, Severity.Critical),
            "the strict profile must promote more findings to Critical than the sandbox one.");
    }

    [Fact]
    public void TheSandboxProfileDoesNotRunTheRulesItDisables()
    {
        var (sandbox, _) = Review(BuiltInProfiles.SandboxService, DataClassification.Unclassified);

        foreach (var id in BuiltInProfiles.SandboxService.DisabledChecks)
        {
            Assert.DoesNotContain(sandbox.Findings, f => f.CheckId == id);
            Assert.DoesNotContain(sandbox.Passed, p => p.CheckId == id);
        }
    }

    [Fact]
    public void TheSandboxProfileStillReportsTheFindingsThatCanHurtTheCluster()
    {
        var (sandbox, _) = Review(BuiltInProfiles.SandboxService, DataClassification.Unclassified);

        foreach (var id in new[] { "CP-SEC-001", "CP-SEC-002", "CP-SEC-007", "CP-SEC-013" })
        {
            var finding = Assert.Single(sandbox.Findings, f => f.CheckId == id);
            Assert.Equal(Severity.Critical, finding.Severity);
        }
    }

    [Fact]
    public void TheSandboxProfileDemotesTheMovingTagRuleToInformational()
    {
        var (sandbox, _) = Review(BuiltInProfiles.SandboxService, DataClassification.Unclassified);

        Assert.Equal(Severity.Info, Assert.Single(sandbox.Findings, f => f.CheckId == "CP-SEC-005").Severity);
    }

    [Fact]
    public void TheSensitiveProfilePromotesTheProtectedRulesToCritical()
    {
        var (sensitive, _) = Review(
            BuiltInProfiles.SensitiveMemberDataService, DataClassification.SensitivePersonalData);

        foreach (var id in new[] { "CP-SEC-008", "CP-REL-004", "CP-REL-005" })
        {
            var finding = Assert.Single(sensitive.Findings, f => f.CheckId == id);
            Assert.Equal(Severity.Critical, finding.Severity);
        }
    }

    [Fact]
    public void TheBatchJobProfileTurnsOffTheServingPathRules()
    {
        var context = CheckTestHarness.Context(
            TestGraph.FromFixture("reliability/cronjob-bad.yaml"), BuiltInProfiles.BatchJob);

        var result = CheckTestHarness.Run(context);

        Assert.DoesNotContain(result.Findings, f => f.CheckId is "CP-REL-001" or "CP-REL-002" or "CP-REL-006");
        Assert.Contains(result.Findings, f => f.CheckId == "CP-REL-009");
    }

    private static int CountOf(CheckRunResult result, Severity severity)
        => result.Findings.Count(f => f.Severity == severity);
}
