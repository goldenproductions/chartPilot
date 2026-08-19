using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ChartPilot.Core.Manifests;

/// <summary>DI registration for the manifest parsing and resource graph layer.</summary>
public static class ManifestsServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IManifestParser"/> and <see cref="IResourceGraphBuilder"/>. Both implementations are
    /// stateless, so they are singletons.
    /// </summary>
    public static IServiceCollection AddChartPilotManifests(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IManifestParser, ManifestParser>();
        services.TryAddSingleton<IResourceGraphBuilder, ResourceGraphBuilder>();

        return services;
    }
}
