using ChartPilot.Core.Review;

namespace ChartPilot.Core.Tests.Review;

public sealed class HelmErrorSourceTests
{
    [Fact]
    public void Extract_finds_the_template_and_position_helm_reported()
    {
        const string stderr =
            "Error: template: member-api/templates/deployment.yaml:24:18: executing \"member-api/templates/deployment.yaml\" " +
            "at <.Values.image.tag>: nil pointer evaluating interface {}.tag";

        Assert.Equal("member-api/templates/deployment.yaml:24:18", HelmErrorSource.Extract(stderr));
    }

    [Fact]
    public void Extract_accepts_a_line_without_a_column()
    {
        Assert.Equal(
            "templates/_helpers.tpl:12",
            HelmErrorSource.Extract("Error: parse error at templates/_helpers.tpl:12: unexpected EOF"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Error: chart requires kubeVersion >= 1.29.0")]
    public void Extract_returns_null_when_no_template_is_named(string? stderr)
    {
        Assert.Null(HelmErrorSource.Extract(stderr));
    }
}
