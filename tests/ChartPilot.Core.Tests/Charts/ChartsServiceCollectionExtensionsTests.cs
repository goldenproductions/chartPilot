using ChartPilot.Core.Charts;
using Microsoft.Extensions.DependencyInjection;

namespace ChartPilot.Core.Tests.Charts;

public class ChartsServiceCollectionExtensionsTests
{
    /// <summary>
    /// A container-free stand-in: <see cref="IServiceCollection"/> is nothing but a list of descriptors,
    /// so registration can be asserted without referencing a DI implementation package.
    /// </summary>
    private sealed class TestServiceCollection : List<ServiceDescriptor>, IServiceCollection
    {
    }

    [Fact]
    public void Registers_the_chart_loader_as_a_singleton()
    {
        var services = new TestServiceCollection();

        var returned = services.AddChartPilotCharts();

        Assert.Same(services, returned);

        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(IChartLoader));
        Assert.Equal(typeof(ChartLoader), descriptor.ImplementationType);
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }

    [Fact]
    public void Registering_twice_does_not_duplicate_the_registration()
    {
        var services = new TestServiceCollection();

        services.AddChartPilotCharts();
        services.AddChartPilotCharts();

        Assert.Single(services, d => d.ServiceType == typeof(IChartLoader));
    }
}
