using ChartPilot.Core.Values;
using Microsoft.Extensions.DependencyInjection;

namespace ChartPilot.Core.Tests.Values;

public class ValuesServiceCollectionExtensionsTests
{
    private sealed class TestServiceCollection : List<ServiceDescriptor>, IServiceCollection
    {
    }

    [Fact]
    public void Registers_the_validator_merger_and_diff_service_as_singletons()
    {
        var services = new TestServiceCollection();

        var returned = services.AddChartPilotValues();

        Assert.Same(services, returned);

        AssertSingleton<IValuesValidator, ValuesValidator>(services);
        AssertSingleton<IValuesMerger, ValuesMerger>(services);
        AssertSingleton<IValuesDiffService, ValuesDiffService>(services);
    }

    [Fact]
    public void Registering_twice_does_not_duplicate_the_registrations()
    {
        var services = new TestServiceCollection();

        services.AddChartPilotValues();
        services.AddChartPilotValues();

        Assert.Equal(3, services.Count);
    }

    private static void AssertSingleton<TService, TImplementation>(IServiceCollection services)
    {
        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(TService));

        Assert.Equal(typeof(TImplementation), descriptor.ImplementationType);
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }
}
