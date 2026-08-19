using ChartPilot.Core.Helm;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace ChartPilot.Api.Tests;

/// <summary>Boots the real API with a stubbed Helm.</summary>
internal sealed class ChartPilotApiFactory : WebApplicationFactory<Program>
{
    public StubHelmClient Helm { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Development);

        // The test chart lives under the temp directory, which is not inside this checkout.
        // Widen the allowlist root the same way a user would when their charts live elsewhere.
        builder.UseSetting("ChartPilot:AllowlistRoot", Path.GetTempPath());

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHelmClient>();
            services.AddSingleton<IHelmClient>(Helm);
        });
    }
}
