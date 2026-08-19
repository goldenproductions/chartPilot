using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ChartPilot.Core.Reporting;

/// <summary>Registers the Markdown report writer and the GitHub Actions workflow generator.</summary>
public static class ReportingServiceCollectionExtensions
{
    public static IServiceCollection AddChartPilotReporting(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IReportWriter, MarkdownReportWriter>();
        services.TryAddSingleton<IWorkflowGenerator, GitHubActionsWorkflowGenerator>();

        return services;
    }
}
