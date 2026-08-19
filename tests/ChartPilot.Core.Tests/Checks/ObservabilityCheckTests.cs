using ChartPilot.Core.Checks;

namespace ChartPilot.Core.Tests.Checks;

public class ObservabilityCheckTests
{
    private const string Bad = "observability/obs-bad.yaml";
    private const string Good = "observability/obs-good.yaml";

    [Fact]
    public void MissingScrapeConfiguration_IsAChartLevelFinding()
    {
        var finding = Assert.Single(CheckTestHarness.FindingsFor("CP-OBS-001", Bad));

        Assert.Null(finding.Resource);
        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.True(CheckTestHarness.Passed("CP-OBS-001", Good));
    }

    [Fact]
    public void PrometheusScrapeAnnotation_SatisfiesTheScrapeRule()
        => Assert.True(CheckTestHarness.Passed("CP-OBS-001", "observability/scrape-annotation.yaml"));

    [Fact]
    public void MissingStandardLabels_AreListedInTheMessage()
    {
        var finding = Assert.Single(CheckTestHarness.FindingsFor("CP-OBS-002", Bad));

        Assert.Equal("Deployment/member-api", finding.Resource?.Key);
        Assert.Equal("metadata.labels", finding.YamlPath);
        Assert.Contains("app.kubernetes.io/name", finding.Message, StringComparison.Ordinal);
        Assert.Contains("app.kubernetes.io/part-of", finding.Message, StringComparison.Ordinal);
        Assert.True(CheckTestHarness.Passed("CP-OBS-002", Good));
    }

    [Fact]
    public void UnnamedContainerPorts_AreReported()
    {
        var finding = Assert.Single(CheckTestHarness.FindingsFor("CP-OBS-003", Bad));

        Assert.Equal(Severity.Info, finding.Severity);
        Assert.Equal("spec.template.spec.containers[0].ports", finding.YamlPath);
        Assert.True(CheckTestHarness.Passed("CP-OBS-003", Good));
    }

    [Fact]
    public void MissingOwnershipAnnotation_IsReported()
    {
        var finding = Assert.Single(CheckTestHarness.FindingsFor("CP-OBS-004", Bad));

        Assert.Equal(Severity.Info, finding.Severity);
        Assert.Equal("metadata.annotations", finding.YamlPath);
        Assert.True(CheckTestHarness.Passed("CP-OBS-004", Good));
    }

    /// <summary>
    /// Spec section 5 lists logging configuration as an observability requirement: the level and the
    /// format are what an operator needs to change during an incident.
    /// </summary>
    [Fact]
    public void MissingLoggingConfiguration_IsReported()
    {
        var finding = Assert.Single(CheckTestHarness.FindingsFor("CP-OBS-005", Bad));

        Assert.Equal("Deployment/member-api", finding.Resource?.Key);
        Assert.Equal(Severity.Info, finding.Severity);
        Assert.Contains("LOG_LEVEL", finding.Message, StringComparison.Ordinal);
        Assert.True(CheckTestHarness.Passed("CP-OBS-005", Good));
    }

    /// <summary>
    /// And correlation: metrics and logs only become one story when a trace id joins them across
    /// services, which has to be configured per service rather than cluster-wide.
    /// </summary>
    [Fact]
    public void MissingCorrelationConfiguration_IsAChartLevelFinding()
    {
        var finding = Assert.Single(CheckTestHarness.FindingsFor("CP-OBS-006", Bad));

        Assert.Null(finding.Resource);
        Assert.Equal(Severity.Info, finding.Severity);
        Assert.Contains("correlation id", finding.Message, StringComparison.Ordinal);
        Assert.True(CheckTestHarness.Passed("CP-OBS-006", Good));
    }
}
