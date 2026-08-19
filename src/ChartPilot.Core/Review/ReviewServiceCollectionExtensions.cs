using ChartPilot.Core.Io;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ChartPilot.Core.Review;

/// <summary>Registers the review pipeline. The caller supplies <c>IHelmClient</c> and the other areas.</summary>
public static class ReviewServiceCollectionExtensions
{
    public static IServiceCollection AddChartPilotReview(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddChartPilotIo();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IRenderService, RenderService>();
        services.TryAddSingleton<IReviewPipeline, ReviewPipeline>();

        return services;
    }
}
