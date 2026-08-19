using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ChartPilot.Core.Checks;

/// <summary>Registers the rule catalog and the engine that runs it.</summary>
public static class ChecksServiceCollectionExtensions
{
    /// <summary>
    /// Registers every <see cref="IResourceCheck"/> in ChartPilot.Core as a singleton, plus the
    /// catalog and the engine. Adding a rule therefore needs no registration code at all: the DI
    /// scan finds it, the catalog orders it, and GET /api/v1/checks publishes it.
    /// </summary>
    /// <remarks>
    /// Requires <c>AddChartPilotProfiles()</c> as well, because the engine resolves severities
    /// through <c>ISeverityResolver</c>.
    /// </remarks>
    public static IServiceCollection AddChartPilotChecks(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        foreach (var checkType in CheckCatalog.DiscoverCheckTypes())
        {
            services.TryAddEnumerable(ServiceDescriptor.Singleton(typeof(IResourceCheck), checkType));
        }

        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        services.TryAddSingleton<ICheckCatalog, CheckCatalog>();
        services.TryAddSingleton<ICheckEngine, CheckEngine>();

        return services;
    }
}
