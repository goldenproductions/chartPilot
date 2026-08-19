using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ChartPilot.Core.Profiles;

/// <summary>Registers the golden path profiles and everything that reads platform metadata.</summary>
public static class ProfilesServiceCollectionExtensions
{
    /// <summary>
    /// Registers the profile catalog, the severity resolver, the suppression loader and the
    /// platform metadata reader. The concrete <see cref="ProfileStore"/> and
    /// <see cref="SuppressionLoader"/> are registered as well as their interfaces, because the CLI
    /// and the API need the extra members those classes expose (loading a profile from a file,
    /// reporting rejected suppressions).
    /// </summary>
    public static IServiceCollection AddChartPilotProfiles(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Explicit factory rather than TryAddSingleton<ProfileStore>(): the container would otherwise
        // pick the IEnumerable<Profile> constructor and hand it an empty catalog.
        services.TryAddSingleton(_ => new ProfileStore());
        services.TryAddSingleton<IProfileStore>(sp => sp.GetRequiredService<ProfileStore>());

        services.TryAddSingleton<SuppressionLoader>();
        services.TryAddSingleton<ISuppressionLoader>(sp => sp.GetRequiredService<SuppressionLoader>());

        services.TryAddSingleton<ISeverityResolver, SeverityResolver>();
        services.TryAddSingleton<IPlatformMetadataReader, PlatformMetadataReader>();

        return services;
    }
}
