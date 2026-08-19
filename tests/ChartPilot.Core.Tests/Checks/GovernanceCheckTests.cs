using ChartPilot.Core.Charts;
using ChartPilot.Core.Checks;
using ChartPilot.Core.Helm;

namespace ChartPilot.Core.Tests.Checks;

public class GovernanceCheckTests
{
    private static ChartModel Chart(bool hasSchema, params ChartDependency[] dependencies)
        => new(
            ChartPath: "/charts/member-api",
            Name: "member-api",
            Version: "0.3.1",
            AppVersion: "1.4.2",
            Description: "Member API",
            Type: "application",
            KubeVersion: null,
            Maintainers: [],
            Dependencies: dependencies,
            ValuesFiles: [],
            HasValuesSchema: hasSchema,
            ValuesSchemaJson: hasSchema ? "{}" : null,
            Templates: [],
            DetectedKinds: [],
            HasSuppressionsFile: false);

    private static CheckRunResult Run(
        ChartModel? chart = null,
        string valuesYaml = "{}",
        string fixture = "governance/ownership-missing.yaml",
        IReadOnlyList<HelmLintMessage>? lint = null,
        bool? lintRan = null)
        => CheckTestHarness.Run(CheckTestHarness.Context(
            TestGraph.FromFixture(fixture),
            valuesYaml: valuesYaml,
            chart: chart,
            lintMessages: lint,
            lintRan: lintRan));

    [Fact]
    public void MissingValuesSchema_IsReported()
    {
        var finding = Assert.Single(Run(Chart(hasSchema: false)).Findings, f => f.CheckId == "CP-GOV-001");

        Assert.Null(finding.Resource);
        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Contains("values.schema.json", finding.Message, StringComparison.Ordinal);

        Assert.Contains(Run(Chart(hasSchema: true)).Passed, p => p.CheckId == "CP-GOV-001");
    }

    [Fact]
    public void ValuesSchemaRule_DoesNotRun_WithoutChartMetadata()
    {
        var result = Run(chart: null);

        Assert.DoesNotContain(result.Findings, f => f.CheckId == "CP-GOV-001");
        Assert.DoesNotContain(result.Passed, p => p.CheckId == "CP-GOV-001");
    }

    [Fact]
    public void UnpinnedDependency_IsReported()
    {
        var chart = Chart(
            hasSchema: true,
            new ChartDependency("redis", "^18.0.0", "https://charts.example.com", null, []),
            new ChartDependency("mongodb", "13.6.4", "https://charts.example.com", null, []));

        var finding = Assert.Single(Run(chart).Findings, f => f.CheckId == "CP-GOV-002");

        Assert.Contains("redis", finding.Message, StringComparison.Ordinal);
        Assert.Contains("^18.0.0", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PinnedDependencies_Pass()
    {
        var chart = Chart(hasSchema: true, new ChartDependency("redis", "18.4.0", null, null, []));

        Assert.Contains(Run(chart).Passed, p => p.CheckId == "CP-GOV-002");
    }

    [Fact]
    public void MissingOwnershipMetadata_IsAChartLevelFinding()
    {
        var finding = Assert.Single(Run().Findings, f => f.CheckId == "CP-GOV-003");

        Assert.Null(finding.Resource);
        Assert.Equal(Severity.Warning, finding.Severity);

        Assert.Contains(
            Run(fixture: "governance/ownership-present.yaml").Passed,
            p => p.CheckId == "CP-GOV-003");
    }

    [Fact]
    public void UndeclaredDataClassification_IsReported()
    {
        var finding = Assert.Single(Run().Findings, f => f.CheckId == "CP-GOV-004");

        Assert.Contains("platform.dataClassification", finding.Message, StringComparison.Ordinal);

        var declared = Run(valuesYaml: "platform:\n  dataClassification: sensitive-personal-data\n");
        Assert.Contains(declared.Passed, p => p.CheckId == "CP-GOV-004");
    }

    [Fact]
    public void HelmLintMessages_AreFoldedIntoFindingsBySeverity()
    {
        var lint = new HelmLintMessage[]
        {
            new(HelmLintSeverity.Error, "templates/deployment.yaml", "unable to parse YAML"),
            new(HelmLintSeverity.Warning, "Chart.yaml", "icon is recommended"),
            new(HelmLintSeverity.Info, ".", "Chart.yaml: directory linted")
        };

        var result = Run(lint: lint);

        var error = Assert.Single(result.Findings, f => f.CheckId == "CP-GOV-006");
        Assert.Equal(Severity.Critical, error.Severity);
        Assert.Contains("unable to parse YAML", error.Message, StringComparison.Ordinal);

        var warning = Assert.Single(result.Findings, f => f.CheckId == "CP-GOV-007");
        Assert.Equal(Severity.Warning, warning.Severity);

        var info = Assert.Single(result.Findings, f => f.CheckId == "CP-GOV-008");
        Assert.Equal(Severity.Info, info.Severity);
    }

    [Fact]
    public void HelmLintRules_DoNotRun_WhenLintDidNot()
    {
        var result = Run();

        Assert.DoesNotContain(result.Findings, f => f.CheckId is "CP-GOV-006" or "CP-GOV-007" or "CP-GOV-008");
        Assert.DoesNotContain(result.Passed, p => p.CheckId is "CP-GOV-006" or "CP-GOV-007" or "CP-GOV-008");
    }

    [Fact]
    public void CleanLint_ReportsTheLintRulesAsPassed()
    {
        var lint = new HelmLintMessage[] { new(HelmLintSeverity.Info, ".", "Chart.yaml: directory linted") };

        var result = Run(lint: lint);

        Assert.Contains(result.Passed, p => p.CheckId == "CP-GOV-006");
        Assert.Contains(result.Passed, p => p.CheckId == "CP-GOV-007");
    }

    /// <summary>
    /// A chart that lints completely clean emits no messages at all. Keying applicability off the
    /// message list made that case indistinguishable from "lint never ran": all three rules were
    /// skipped, so a clean lint never appeared in the passed list or in the governance score.
    /// </summary>
    [Fact]
    public void SilentButExecutedLint_ReportsAllThreeLintRulesAsPassed()
    {
        var result = Run(lint: [], lintRan: true);

        Assert.DoesNotContain(result.Findings, f => f.CheckId is "CP-GOV-006" or "CP-GOV-007" or "CP-GOV-008");
        Assert.Contains(result.Passed, p => p.CheckId == "CP-GOV-006");
        Assert.Contains(result.Passed, p => p.CheckId == "CP-GOV-007");
        Assert.Contains(result.Passed, p => p.CheckId == "CP-GOV-008");
    }
}
