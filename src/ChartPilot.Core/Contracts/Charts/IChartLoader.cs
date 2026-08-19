namespace ChartPilot.Core.Charts;

/// <summary>Reads a chart directory into a <see cref="ChartModel"/>. Read-only; never writes to the chart.</summary>
public interface IChartLoader
{
    /// <summary>Loads the chart rooted at <paramref name="chartDirectory"/>.</summary>
    ChartModel Load(string chartDirectory);

    /// <summary>True when the path exists and contains a Chart.yaml.</summary>
    bool IsChartDirectory(string path);
}
