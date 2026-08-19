using System.Reflection;

namespace ChartPilot.Core;

/// <summary>
/// A handle on the ChartPilot.Core assembly, used by the composition root to DI-scan for
/// <see cref="Checks.IResourceCheck"/> implementations. There is deliberately no AddChartPilotCore()
/// here: each feature area registers its own services, and the API host composes them.
/// </summary>
public static class CoreAssemblyMarker
{
    public static readonly Assembly Assembly = typeof(CoreAssemblyMarker).Assembly;
}
