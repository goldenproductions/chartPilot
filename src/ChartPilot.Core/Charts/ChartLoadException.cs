namespace ChartPilot.Core.Charts;

/// <summary>
/// Thrown when a directory cannot be read as a Helm chart: it does not exist, it has no
/// <c>Chart.yaml</c>, or that file cannot be read or parsed.
/// </summary>
public sealed class ChartLoadException : Exception
{
    public ChartLoadException(string message, string? chartPath = null)
        : base(message)
    {
        ChartPath = chartPath;
    }

    public ChartLoadException(string message, string? chartPath, Exception innerException)
        : base(message, innerException)
    {
        ChartPath = chartPath;
    }

    /// <summary>The directory ChartPilot was asked to load, when it is known.</summary>
    public string? ChartPath { get; }
}
