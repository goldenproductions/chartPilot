namespace ChartPilot.Api.Endpoints;

/// <summary>Wires up the /api/v1 surface described in docs/architecture.md §7.</summary>
public static class ApiEndpoints
{
    public const string BasePath = "/api/v1";

    public static IEndpointRouteBuilder MapChartPilotApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup(BasePath);

        api.MapEnvironmentEndpoints();
        api.MapWorkspaceEndpoints();
        api.MapReviewEndpoints();
        api.MapCatalogEndpoints();

        return app;
    }
}
