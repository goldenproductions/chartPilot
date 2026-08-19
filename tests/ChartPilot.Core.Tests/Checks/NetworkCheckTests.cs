using ChartPilot.Core.Checks;
using ChartPilot.Core.Profiles;

namespace ChartPilot.Core.Tests.Checks;

public class NetworkCheckTests
{
    private const string Bad = "network/istio-bad.yaml";
    private const string Good = "network/istio-good.yaml";

    [Fact]
    public void IstioRules_DoNotRun_OnAChartWithNoMeshResources()
    {
        var result = CheckTestHarness.Run(
            CheckTestHarness.Context(TestGraph.FromFixture("reliability/deployment-good.yaml")));

        Assert.DoesNotContain(result.Findings, f => f.CheckId.StartsWith("CP-NET-", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Passed, p => p.CheckId.StartsWith("CP-NET-", StringComparison.Ordinal));
    }

    [Fact]
    public void UnresolvedGateway_IsReported()
    {
        var finding = Assert.Single(CheckTestHarness.FindingsFor("CP-NET-001", Bad));

        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Equal("VirtualService/member-api", finding.Resource?.Key);
        Assert.Equal("spec.gateways[0]", finding.YamlPath);
        Assert.True(CheckTestHarness.Passed("CP-NET-001", Good));
    }

    [Fact]
    public void PublicRouteWithoutAuthorizationPolicy_IsCritical()
    {
        var finding = Assert.Single(CheckTestHarness.FindingsFor("CP-NET-002", Bad));

        Assert.Equal(Severity.Critical, finding.Severity);
        Assert.Equal("spec.gateways", finding.YamlPath);
        Assert.True(CheckTestHarness.Passed("CP-NET-002", Good));
    }

    /// <summary>
    /// When the route's destination is not a Service this chart renders, an unrelated policy that
    /// selects a different workload must not silence the headline finding of the whole tool. Only a
    /// namespace-wide policy (one with no selector) can cover a workload the chart cannot see.
    /// </summary>
    [Fact]
    public void PublicRouteToAnUnrenderedDestination_IsNotCoveredByAnUnrelatedPolicy()
    {
        var finding = Assert.Single(
            CheckTestHarness.FindingsFor("CP-NET-002", "network/external-destination.yaml"));

        Assert.Equal("VirtualService/member-api", finding.Resource?.Key);
        Assert.Contains("does not render", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingStrictMtls_IsAChartLevelFinding()
    {
        var finding = Assert.Single(CheckTestHarness.FindingsFor("CP-NET-003", Bad));

        Assert.Null(finding.Resource);
        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Contains("STRICT", finding.Message, StringComparison.Ordinal);
        Assert.True(CheckTestHarness.Passed("CP-NET-003", Good));
    }

    [Fact]
    public void MissingStrictMtls_IsPromotedToCritical_ForSensitivePersonalData()
    {
        var findings = CheckTestHarness.FindingsFor(
            "CP-NET-003", Bad, BuiltInProfiles.Default, DataClassification.SensitivePersonalData);

        Assert.Equal(Severity.Critical, Assert.Single(findings).Severity);
    }

    [Fact]
    public void RoutedServiceWithoutDestinationRule_IsReported()
    {
        var finding = Assert.Single(CheckTestHarness.FindingsFor("CP-NET-004", Bad));

        Assert.Equal("VirtualService/member-api", finding.Resource?.Key);
        Assert.Contains("member-api", finding.Message, StringComparison.Ordinal);
        Assert.True(CheckTestHarness.Passed("CP-NET-004", Good));
    }

    [Fact]
    public void RouteWithoutTimeout_IsReported()
    {
        var finding = Assert.Single(CheckTestHarness.FindingsFor("CP-NET-005", Bad));

        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Equal("spec.http[0]", finding.YamlPath);
        Assert.True(CheckTestHarness.Passed("CP-NET-005", Good));
    }

    [Fact]
    public void RouteWithoutRetries_IsReported()
    {
        var finding = Assert.Single(CheckTestHarness.FindingsFor("CP-NET-006", Bad));

        Assert.Equal(Severity.Info, finding.Severity);
        Assert.Equal("spec.http[0]", finding.YamlPath);
        Assert.True(CheckTestHarness.Passed("CP-NET-006", Good));
    }

    [Fact]
    public void DanglingRouteDestination_IsReported()
    {
        var finding = Assert.Single(CheckTestHarness.FindingsFor("CP-NET-007", "network/dangling-route.yaml"));

        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Equal("spec.http[0].route[0].destination.host", finding.YamlPath);
        Assert.Contains("member-api-typo", finding.Message, StringComparison.Ordinal);
        Assert.True(CheckTestHarness.Passed("CP-NET-007", Good));
    }

    [Fact]
    public void AllowAllAuthorizationPolicy_IsCritical()
    {
        var finding = Assert.Single(
            CheckTestHarness.FindingsFor("CP-NET-008", "network/authorization-allow-all.yaml"));

        Assert.Equal(Severity.Critical, finding.Severity);
        Assert.Equal("AuthorizationPolicy/allow-everything", finding.Resource?.Key);
        Assert.Equal("spec.rules[0]", finding.YamlPath);
        Assert.True(CheckTestHarness.Passed("CP-NET-008", Good));
    }

    /// <summary>
    /// In Istio an ALLOW policy with no rules matches nothing and therefore denies everything —
    /// the deny-all default every hardened namespace ships. Reporting it as "allows every caller"
    /// flagged the strictest possible policy as the worst possible one.
    /// </summary>
    [Fact]
    public void DenyAllAuthorizationPolicy_IsNotAFinding()
    {
        Assert.Empty(CheckTestHarness.FindingsFor("CP-NET-008", "network/authorization-deny-all.yaml"));
        Assert.True(CheckTestHarness.Passed("CP-NET-008", "network/authorization-deny-all.yaml"));
    }
}
