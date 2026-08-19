using ChartPilot.Api.Contracts;
using ChartPilot.Core.Checks;
using ChartPilot.Core.Profiles;

namespace ChartPilot.Api.Endpoints;

/// <summary>The two read-only catalogs the GUI needs: golden path profiles and the rule list.</summary>
public static class CatalogEndpoints
{
    public static IEndpointRouteBuilder MapCatalogEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/profiles", (IProfileStore profiles) =>
        {
            var defaultId = profiles.Default.Id;

            return Results.Ok(profiles.Profiles
                .Select(p => DtoMapper.ToProfileDto(p, string.Equals(p.Id, defaultId, StringComparison.Ordinal)))
                .ToList());
        })
        .WithName("GetProfiles");

        app.MapGet("/checks", (ICheckCatalog catalog) => Results.Ok(catalog.Descriptors
            .OrderBy(d => d.Id, StringComparer.Ordinal)
            .Select(DtoMapper.ToCheckDto)
            .ToList()))
        .WithName("GetChecks");

        return app;
    }
}
