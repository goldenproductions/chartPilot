using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ChartPilot.Core.Io;

/// <summary>Registers the filesystem abstraction Core reads charts and values through.</summary>
public static class IoServiceCollectionExtensions
{
    public static IServiceCollection AddChartPilotIo(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IFileSystem, PhysicalFileSystem>();

        return services;
    }
}
