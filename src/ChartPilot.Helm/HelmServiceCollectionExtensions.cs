using ChartPilot.Core.Helm;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ChartPilot.Helm;

/// <summary>Registers the helm integration. Called by both the API host and the CLI.</summary>
public static class HelmServiceCollectionExtensions
{
    /// <summary>Registers the helm client, binding options from the <c>ChartPilot</c> configuration section.</summary>
    public static IServiceCollection AddChartPilotHelm(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<ChartPilotHelmOptions>()
            .Bind(configuration.GetSection(ChartPilotHelmOptions.SectionName));

        return services.AddHelmCore();
    }

    /// <summary>Registers the helm client with options configured in code (the CLI has no IConfiguration).</summary>
    public static IServiceCollection AddChartPilotHelm(this IServiceCollection services, Action<ChartPilotHelmOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddOptions<ChartPilotHelmOptions>().Configure(configure);

        return services.AddHelmCore();
    }

    private static IServiceCollection AddHelmCore(this IServiceCollection services)
    {
        services.AddOptions();

        services.TryAddSingleton<IHelmEnvironment>(SystemHelmEnvironment.Instance);

        services.TryAddSingleton<IProcessRunner>(sp => new ProcessRunner(
            sp.GetService<ILogger<ProcessRunner>>() ?? NullLogger<ProcessRunner>.Instance));

        services.TryAddSingleton(sp => new HelmLocator(
            sp.GetRequiredService<IProcessRunner>(),
            sp.GetRequiredService<IOptions<ChartPilotHelmOptions>>(),
            sp.GetService<ILogger<HelmLocator>>() ?? NullLogger<HelmLocator>.Instance,
            sp.GetRequiredService<IHelmEnvironment>()));

        services.TryAddSingleton<IHelmClient>(sp => new HelmClient(
            sp.GetRequiredService<IProcessRunner>(),
            sp.GetRequiredService<HelmLocator>(),
            sp.GetRequiredService<IOptions<ChartPilotHelmOptions>>(),
            sp.GetService<ILogger<HelmClient>>() ?? NullLogger<HelmClient>.Instance));

        return services;
    }
}
