using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ChartPilot.Core.Scoring;

/// <summary>Registers the platform scorer.</summary>
public static class ScoringServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IScorer"/>. The scorer resolves finding categories through the check
    /// catalog when one is registered, so call this after <c>AddChartPilotChecks()</c>.
    /// </summary>
    public static IServiceCollection AddChartPilotScoring(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IScorer>(sp => new Scorer(sp.GetService<Checks.ICheckCatalog>()));

        return services;
    }
}
