using ChartPilot.Core.Reporting;

namespace ChartPilot.Core.Tests.Reporting;

public sealed class GitHubActionsWorkflowGeneratorTests
{
    private readonly GitHubActionsWorkflowGenerator _generator = new();

    private static WorkflowOptions Sample() => new(
        ChartPath: "./chart",
        ChartName: "member-api",
        Environments: ["dev", "test", "prod"],
        ProfileId: "sensitive-member-data-service",
        FailOn: "critical",
        Namespace: "member-platform");

    [Fact]
    public void Generate_matches_the_snapshot()
    {
        var actual = _generator.Generate(Sample());

        Assert.Equal(
            MarkdownReportWriterTests.ReadFixture("deploy-workflow.yml"),
            MarkdownReportWriterTests.Normalize(actual));
    }

    [Fact]
    public void Generate_keeps_github_expressions_verbatim()
    {
        var yaml = _generator.Generate(Sample());

        Assert.Contains("-f values-${{ inputs.environment }}.yaml", yaml, StringComparison.Ordinal);
        Assert.Contains("environment: ${{ inputs.environment }}", yaml, StringComparison.Ordinal);
        Assert.DoesNotContain("'${{", yaml, StringComparison.Ordinal);
        Assert.DoesNotContain("\"${{", yaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_lists_every_discovered_environment_as_a_choice_option()
    {
        var yaml = _generator.Generate(Sample() with { Environments = ["dev", "staging"] });

        Assert.Contains("        options:\n          - dev\n          - staging\n", yaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_falls_back_to_the_conventional_environments_when_none_were_discovered()
    {
        var yaml = _generator.Generate(Sample() with { Environments = [] });

        Assert.Contains("          - dev\n          - test\n          - prod\n", yaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_rejects_an_unknown_fail_on_level_rather_than_emitting_it()
    {
        var yaml = _generator.Generate(Sample() with { FailOn = "catastrophic" });

        Assert.Contains("--fail-on critical", yaml, StringComparison.Ordinal);
        Assert.DoesNotContain("catastrophic", yaml, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("chart", "./chart")]
    [InlineData("charts\\member-api", "./charts/member-api")]
    [InlineData("./charts/member-api", "./charts/member-api")]
    public void Generate_normalizes_the_chart_path_for_a_linux_runner(string input, string expected)
    {
        var yaml = _generator.Generate(Sample() with { ChartPath = input });

        Assert.Contains($"run: helm lint {expected}\n", yaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_gates_deploy_on_validate()
    {
        var yaml = _generator.Generate(Sample());

        Assert.Contains("  deploy:\n    name: Deploy\n    needs: validate\n", yaml, StringComparison.Ordinal);
    }
}
