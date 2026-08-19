using ChartPilot.Core.Checks;
using ChartPilot.Core.Profiles;

namespace ChartPilot.Core.Tests.Checks;

public class CheckEngineTests
{
    private const string Fixture = "security/workload-bad.yaml";

    private static CheckContext Context(Profile? profile = null)
        => CheckTestHarness.Context(TestGraph.FromFixture(Fixture), profile);

    [Fact]
    public void AMatchedSuppressionMovesTheFindingOutOfTheFindingsList()
    {
        var suppression = new Suppression("CP-SEC-001", null, "Vendor image; tracked in PLAT-412", null);

        var result = CheckTestHarness.Run(Context(), [suppression]);

        Assert.DoesNotContain(result.Findings, f => f.CheckId == "CP-SEC-001");

        var suppressed = Assert.Single(result.Suppressed, s => s.Finding.CheckId == "CP-SEC-001");
        Assert.Equal("Vendor image; tracked in PLAT-412", suppressed.Reason);
        Assert.Equal(Severity.Critical, suppressed.Finding.Severity);
    }

    [Fact]
    public void AResourceScopedSuppressionOnlyMatchesThatResource()
    {
        var matching = new Suppression("CP-SEC-001", "Deployment/member-api", "Known exception", null);
        var other = new Suppression("CP-SEC-002", "Deployment/somewhere-else", "Different workload", null);

        var result = CheckTestHarness.Run(Context(), [matching, other]);

        Assert.Contains(result.Suppressed, s => s.Finding.CheckId == "CP-SEC-001");
        Assert.Contains(result.Findings, f => f.CheckId == "CP-SEC-002");
    }

    [Fact]
    public void AnExpiredSuppressionLeavesTheFindingInPlaceAndRaisesGov005()
    {
        var expired = new Suppression(
            "CP-SEC-001", null, "Temporary while we rebuild the image", new DateOnly(2026, 1, 1));

        var result = CheckTestHarness.Run(Context(), [expired]);

        Assert.Contains(result.Findings, f => f.CheckId == "CP-SEC-001");
        Assert.DoesNotContain(result.Suppressed, s => s.Finding.CheckId == "CP-SEC-001");

        var hygiene = Assert.Single(result.Findings, f => f.CheckId == "CP-GOV-005");
        Assert.Contains("expired on 2026-01-01", hygiene.Message, StringComparison.Ordinal);
        Assert.Contains("CP-SEC-001", hygiene.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ASuppressionWithoutAReasonIsRejectedAndRaisesGov005()
    {
        var noReason = new Suppression("CP-SEC-001", null, "   ", null);

        var result = CheckTestHarness.Run(Context(), [noReason]);

        Assert.Contains(result.Findings, f => f.CheckId == "CP-SEC-001");

        var hygiene = Assert.Single(result.Findings, f => f.CheckId == "CP-GOV-005");
        Assert.Contains("has no reason", hygiene.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidSuppressionsReportTheHygieneRuleAsPassed()
    {
        var valid = new Suppression("CP-SEC-004", null, "Vendor image needs a writable root", new DateOnly(2027, 1, 1));

        var result = CheckTestHarness.Run(Context(), [valid]);

        Assert.Contains(result.Passed, p => p.CheckId == "CP-GOV-005");
        Assert.DoesNotContain(result.Findings, f => f.CheckId == "CP-GOV-005");
    }

    [Fact]
    public void TheHygieneRuleIsSilentWhenThereAreNoSuppressionsAtAll()
    {
        var result = CheckTestHarness.Run(Context());

        Assert.DoesNotContain(result.Findings, f => f.CheckId == "CP-GOV-005");
        Assert.DoesNotContain(result.Passed, p => p.CheckId == "CP-GOV-005");
    }

    [Fact]
    public void DisabledChecksDoNotRunAtAll()
    {
        var profile = BuiltInProfiles.Default with { DisabledChecks = ["CP-SEC-001", "CP-SEC-002"] };

        var result = CheckTestHarness.Run(Context(profile));

        Assert.DoesNotContain(result.Findings, f => f.CheckId is "CP-SEC-001" or "CP-SEC-002");
        Assert.DoesNotContain(result.Passed, p => p.CheckId is "CP-SEC-001" or "CP-SEC-002");
        Assert.Contains(result.Findings, f => f.CheckId == "CP-SEC-005");
    }

    [Fact]
    public void TheEngineOverwritesTheSeverityTheRuleEmitted()
    {
        // CP-SEC-004 defaults to Warning; the legacy profile overrides it to Info.
        Assert.Equal(
            Severity.Warning,
            Assert.Single(CheckTestHarness.FindingsFor("CP-SEC-004", Fixture)).Severity);

        Assert.Equal(
            Severity.Info,
            Assert.Single(CheckTestHarness.FindingsFor("CP-SEC-004", Fixture, BuiltInProfiles.LegacyIntegrationService))
                .Severity);
    }

    [Fact]
    public void FindingsCarryTheSourceTemplateOfTheResourceTheyCameFrom()
    {
        var finding = Assert.Single(CheckTestHarness.FindingsFor("CP-SEC-002", Fixture));

        Assert.Equal("workload-bad.yaml", finding.SourceTemplate);
    }
}
