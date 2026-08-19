using ChartPilot.Core.Helm;
using ChartPilot.Helm;

namespace ChartPilot.Helm.Tests;

public sealed class HelmLintParserTests
{
    private const string ChartPath = "/repo/charts/member-api";

    public static TheoryData<string, HelmLintSeverity, string, string> Lines => new()
    {
        {
            "[INFO] Chart.yaml: icon is recommended",
            HelmLintSeverity.Info,
            "Chart.yaml",
            "icon is recommended"
        },
        {
            "[WARNING] templates/deployment.yaml: object name does not conform to Kubernetes naming requirements",
            HelmLintSeverity.Warning,
            "templates/deployment.yaml",
            "object name does not conform to Kubernetes naming requirements"
        },
        {
            "[ERROR] templates/: parse error at (member-api/templates/svc.yaml:14): unexpected EOF",
            HelmLintSeverity.Error,
            "templates/",
            "parse error at (member-api/templates/svc.yaml:14): unexpected EOF"
        },
        {
            "[ERROR] Chart.yaml: version is required",
            HelmLintSeverity.Error,
            "Chart.yaml",
            "version is required"
        },
        {
            "[INFO] : chart metadata is missing a home url",
            HelmLintSeverity.Info,
            ChartPath,
            "chart metadata is missing a home url"
        },
        {
            "[ERROR] unable to load chart: no such file or directory",
            HelmLintSeverity.Error,
            ChartPath,
            "unable to load chart: no such file or directory"
        }
    };

    [Theory]
    [MemberData(nameof(Lines))]
    public void Parse_reads_a_single_line(string line, HelmLintSeverity severity, string file, string message)
    {
        var parsed = Assert.Single(HelmLintParser.Parse(line, ChartPath));

        Assert.Equal(severity, parsed.Severity);
        Assert.Equal(file, parsed.File);
        Assert.Equal(message, parsed.Message);
    }

    [Fact]
    public void Parse_ignores_the_header_summary_and_blank_lines()
    {
        const string output = """
            ==> Linting ./charts/member-api

            [INFO] Chart.yaml: icon is recommended
            [WARNING] templates/deployment.yaml: no resource limits are set

            1 chart(s) linted, 0 chart(s) failed
            """;

        var messages = HelmLintParser.Parse(output, ChartPath);

        Assert.Equal(2, messages.Count);
        Assert.Equal(HelmLintSeverity.Info, messages[0].Severity);
        Assert.Equal(HelmLintSeverity.Warning, messages[1].Severity);
        Assert.Equal("no resource limits are set", messages[1].Message);
    }

    [Fact]
    public void Parse_handles_windows_line_endings()
    {
        const string output = "==> Linting ./chart\r\n[INFO] Chart.yaml: icon is recommended\r\n\r\n1 chart(s) linted, 0 chart(s) failed\r\n";

        var message = Assert.Single(HelmLintParser.Parse(output, ChartPath));

        Assert.Equal("Chart.yaml", message.File);
        Assert.Equal("icon is recommended", message.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n\t ")]
    [InlineData("1 chart(s) linted, 1 chart(s) failed")]
    [InlineData("Error: 1 chart(s) linted, 1 chart(s) failed")]
    public void Parse_returns_nothing_for_output_without_messages(string? output)
    {
        Assert.Empty(HelmLintParser.Parse(output, ChartPath));
    }

    [Fact]
    public void ParseSeverity_is_case_insensitive_and_defaults_to_info()
    {
        Assert.Equal(HelmLintSeverity.Error, HelmLintParser.ParseSeverity("error"));
        Assert.Equal(HelmLintSeverity.Warning, HelmLintParser.ParseSeverity(" Warning "));
        Assert.Equal(HelmLintSeverity.Info, HelmLintParser.ParseSeverity("DEBUG"));
    }
}
