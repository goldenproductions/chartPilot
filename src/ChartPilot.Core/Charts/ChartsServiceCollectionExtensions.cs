using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ChartPilot.Core.Charts;

/// <summary>Registers the chart-loading half of the pipeline.</summary>
public static class ChartsServiceCollectionExtensions
{
    /// <summary>Adds <see cref="IChartLoader"/>. Safe to call more than once.</summary>
    public static IServiceCollection AddChartPilotCharts(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IChartLoader, ChartLoader>();

        return services;
    }
}
