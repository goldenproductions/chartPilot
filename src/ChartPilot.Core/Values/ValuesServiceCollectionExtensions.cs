using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ChartPilot.Core.Values;

/// <summary>Registers the values half of the pipeline: validation, merging and the environment diff.</summary>
public static class ValuesServiceCollectionExtensions
{
    /// <summary>
    /// Adds <see cref="IValuesValidator"/>, <see cref="IValuesMerger"/> and <see cref="IValuesDiffService"/>.
    /// All three are stateless singletons. Safe to call more than once.
    /// </summary>
    public static IServiceCollection AddChartPilotValues(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IValuesValidator, ValuesValidator>();
        services.TryAddSingleton<IValuesMerger, ValuesMerger>();
        services.TryAddSingleton<IValuesDiffService, ValuesDiffService>();

        return services;
    }
}
