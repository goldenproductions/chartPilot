using System.Reflection;
using ChartPilot.Api.Contracts;
using ChartPilot.Core.Helm;
using ChartPilot.Helm;
using Microsoft.Extensions.Options;

namespace ChartPilot.Api.Endpoints;

/// <summary>
/// GET /api/v1/environment. This is the endpoint the GUI calls first: a missing Helm becomes an
/// actionable banner here rather than a cryptic failure on the first render.
/// </summary>
public static class EnvironmentEndpoints
{
    public static IEndpointRouteBuilder MapEnvironmentEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/environment", async (
            IHelmClient helm,
            IOptions<ChartPilotHelmOptions> options,
            CancellationToken ct) =>
        {
            var executable = await helm.ResolveAsync(ct).ConfigureAwait(false);

            return Results.Ok(new EnvironmentDto(
                executable.IsAvailable,
                executable.Path,
                executable.Version,
                executable.Error,
                executable.ResolutionSource,
                options.Value.ResolveAllowlistRoot(),
                Version));
        })
        .WithName("GetEnvironment");

        return app;
    }

    private static string Version =>
        typeof(EnvironmentEndpoints).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(EnvironmentEndpoints).Assembly.GetName().Version?.ToString()
        ?? "0.0.0";
}
