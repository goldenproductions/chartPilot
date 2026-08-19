namespace ChartPilot.Core.Tests.Charts;

/// <summary>Locates the chart fixture directories copied next to the test assembly.</summary>
internal static class ChartFixtures
{
    public const string FullChart = "full-chart";
    public const string MinimalChart = "minimal-chart";
    public const string BrokenChart = "broken-chart";
    public const string NotAChart = "not-a-chart";

    public static string Root => Path.Combine(AppContext.BaseDirectory, "Fixtures", "Charts");

    public static string Dir(string name) => Path.Combine(Root, name);
}
