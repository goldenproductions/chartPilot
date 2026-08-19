using ChartPilot.Core.Checks;

namespace ChartPilot.Core.Tests.Checks;

public class CertificateCheckTests
{
    private const string Bad = "certificates/certificate-bad.yaml";
    private const string Good = "certificates/certificate-good.yaml";

    [Fact]
    public void CertificateRules_DoNotRun_WithoutACertificate()
    {
        var result = CheckTestHarness.Run(
            CheckTestHarness.Context(TestGraph.FromFixture("reliability/deployment-good.yaml")));

        Assert.DoesNotContain(result.Findings, f => f.CheckId.StartsWith("CP-CERT-", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Passed, p => p.CheckId.StartsWith("CP-CERT-", StringComparison.Ordinal));
    }

    [Fact]
    public void MissingRenewBefore_IsReported()
    {
        var finding = Assert.Single(CheckTestHarness.FindingsFor("CP-CERT-001", Bad));

        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Equal("Certificate/member-api-tls", finding.Resource?.Key);
        Assert.Equal("spec.renewBefore", finding.YamlPath);
        Assert.True(CheckTestHarness.Passed("CP-CERT-001", Good));
    }

    [Fact]
    public void DurationLongerThanNinetyDays_IsReported()
    {
        var finding = Assert.Single(CheckTestHarness.FindingsFor("CP-CERT-002", Bad));

        Assert.Equal("spec.duration", finding.YamlPath);
        Assert.Contains("365 days", finding.Message, StringComparison.Ordinal);

        // Exactly 2160h is the ceiling, not a violation of it.
        Assert.True(CheckTestHarness.Passed("CP-CERT-002", Good));
    }

    [Fact]
    public void UnknownIssuer_IsReported()
    {
        var finding = Assert.Single(CheckTestHarness.FindingsFor("CP-CERT-003", Bad));

        Assert.Equal("spec.issuerRef.name", finding.YamlPath);
        Assert.Contains("ClusterIssuer/letsencrypt-prod", finding.Message, StringComparison.Ordinal);
        Assert.True(CheckTestHarness.Passed("CP-CERT-003", Good));
    }

    [Fact]
    public void UnreferencedTlsSecret_IsReported()
    {
        var finding = Assert.Single(CheckTestHarness.FindingsFor("CP-CERT-004", Bad));

        Assert.Equal(Severity.Info, finding.Severity);
        Assert.Equal("spec.secretName", finding.YamlPath);

        // The compliant fixture consumes the secret from an Istio Gateway credentialName.
        Assert.True(CheckTestHarness.Passed("CP-CERT-004", Good));
    }

    [Fact]
    public void RenewBeforeNotShorterThanDuration_IsCritical()
    {
        var finding = Assert.Single(
            CheckTestHarness.FindingsFor("CP-CERT-005", "certificates/renew-before-too-long.yaml"));

        Assert.Equal(Severity.Critical, finding.Severity);
        Assert.Equal("spec.renewBefore", finding.YamlPath);
        Assert.True(CheckTestHarness.Passed("CP-CERT-005", Good));
    }
}
