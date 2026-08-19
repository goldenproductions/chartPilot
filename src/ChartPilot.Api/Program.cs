using System.Net;
using System.Text.Json.Serialization;
using ChartPilot.Api.Endpoints;
using ChartPilot.Api.Infrastructure;
using ChartPilot.Api.Workspaces;
using ChartPilot.Core.Charts;
using ChartPilot.Core.Checks;
using ChartPilot.Core.Manifests;
using ChartPilot.Core.Profiles;
using ChartPilot.Core.Reporting;
using ChartPilot.Core.Review;
using ChartPilot.Core.Scoring;
using ChartPilot.Core.Values;
using ChartPilot.Helm;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    WebRootPath = SpaHosting.ResolveWebRoot()
});

// ChartPilot renders arbitrary Go templates from charts the user points it at. It is a local tool,
// not a service, so it binds loopback only — and refuses to start if it is told otherwise.
var configuredUrls = builder.Configuration["urls"] ?? Environment.GetEnvironmentVariable("ASPNETCORE_URLS");

if (string.IsNullOrWhiteSpace(configuredUrls))
{
    // 5080 in every environment. Port 5173 belongs to the Vite dev server, which sets
    // strictPort, so binding the API there would stop `npm run dev` from starting at all.
    builder.WebHost.UseUrls("http://127.0.0.1:5080");
}
else if (!SpaHosting.IsLoopbackOnly(configuredUrls))
{
    throw new InvalidOperationException(
        $"ChartPilot binds loopback only, but was configured with '{configuredUrls}'. " +
        "Use 127.0.0.1 or localhost.");
}

// The chart the user opens almost never lives under the API's own working directory, so an
// unconfigured allowlist root would reject every real chart at render time. Resolve a usable
// default once, and write it back into configuration so the Helm options and GET /environment
// cannot disagree about what it is.
builder.Configuration[$"{ChartPilotHelmOptions.SectionName}:AllowlistRoot"] =
    AllowlistRoots.Resolve(builder.Configuration[$"{ChartPilotHelmOptions.SectionName}:AllowlistRoot"]);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ChartPilotExceptionHandler>();
builder.Services.AddMemoryCache();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<WorkspaceStore>();

builder.Services
    .AddChartPilotCharts()
    .AddChartPilotValues()
    .AddChartPilotManifests()
    .AddChartPilotChecks()
    .AddChartPilotProfiles()
    .AddChartPilotScoring()
    .AddChartPilotReview()
    .AddChartPilotReporting()
    .AddChartPilotHelm(builder.Configuration);

if (builder.Environment.IsDevelopment())
{
    // The Vite dev server, and nothing else.
    builder.Services.AddCors(options => options.AddPolicy(SpaHosting.DevCorsPolicy, policy => policy
        .WithOrigins("http://127.0.0.1:5173", "http://localhost:5173")
        .AllowAnyHeader()
        .AllowAnyMethod()));
}

var app = builder.Build();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseCors(SpaHosting.DevCorsPolicy);
}

if (!string.IsNullOrEmpty(app.Environment.WebRootPath) && Directory.Exists(app.Environment.WebRootPath))
{
    app.UseDefaultFiles();
    app.UseStaticFiles();
}

app.MapChartPilotApi();

// Everything that is not /api is the SPA; /api itself must 404 as a problem document rather than
// silently returning index.html, which would turn a typo into a confusing HTML parse error.
app.MapFallback((HttpContext context) =>
{
    if (context.Request.Path.StartsWithSegments(ApiEndpoints.BasePath, StringComparison.OrdinalIgnoreCase) ||
        context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
    {
        return Problems.UnknownEndpoint();
    }

    var index = SpaHosting.IndexFile(app.Environment.WebRootPath);

    return index is null
        ? Results.NotFound()
        : Results.File(index, "text/html");
});

app.Run();

/// <summary>SPA hosting and binding helpers, kept out of the top-level statements for testability.</summary>
internal static class SpaHosting
{
    public const string DevCorsPolicy = "chartpilot-dev";

    /// <summary>
    /// The Vite build output. wwwroot next to the binary wins; otherwise the repo's
    /// src/chartpilot-web/dist is used, which is what makes <c>dotnet run</c> from a checkout work.
    /// </summary>
    public static string? ResolveWebRoot()
    {
        var configured = Environment.GetEnvironmentVariable("CHARTPILOT_WEBROOT");

        if (!string.IsNullOrWhiteSpace(configured) && Directory.Exists(configured))
        {
            return Path.GetFullPath(configured);
        }

        var wwwroot = Path.Combine(AppContext.BaseDirectory, "wwwroot");

        if (Directory.Exists(wwwroot))
        {
            return wwwroot;
        }

        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        for (var depth = 0; depth < 8 && directory is not null; depth++)
        {
            var candidate = Path.Combine(directory.FullName, "src", "chartpilot-web", "dist");

            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return null;
    }

    public static string? IndexFile(string? webRoot)
    {
        if (string.IsNullOrEmpty(webRoot))
        {
            return null;
        }

        var index = Path.Combine(webRoot, "index.html");

        return File.Exists(index) ? index : null;
    }

    /// <summary>True when every configured URL binds a loopback address.</summary>
    public static bool IsLoopbackOnly(string urls)
    {
        foreach (var url in urls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                return false;
            }

            var isLoopback =
                uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                (IPAddress.TryParse(uri.Host, out var address) && IPAddress.IsLoopback(address));

            if (!isLoopback)
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>Exposed so WebApplicationFactory&lt;Program&gt; can bind the host in the contract tests.</summary>
public partial class Program { }
