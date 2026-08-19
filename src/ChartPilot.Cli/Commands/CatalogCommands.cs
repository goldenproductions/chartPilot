using ChartPilot.Cli.CommandLine;
using ChartPilot.Core.Checks;
using ChartPilot.Core.Profiles;
using Microsoft.Extensions.DependencyInjection;

namespace ChartPilot.Cli.Commands;

/// <summary><c>chartpilot profiles</c> and <c>chartpilot checks</c> — the two catalogs, listed.</summary>
internal static class CatalogCommands
{
    public static int Profiles(ServiceProvider provider, TextWriter output)
    {
        using (provider)
        {
            var store = provider.GetRequiredService<IProfileStore>();
            var defaultId = store.Default.Id;

            foreach (var profile in store.Profiles.OrderBy(p => p.Id, StringComparer.Ordinal))
            {
                var marker = string.Equals(profile.Id, defaultId, StringComparison.Ordinal) ? " (default)" : string.Empty;
                output.WriteLine($"{profile.Id}{marker}");
                output.WriteLine($"  {profile.Name} — {profile.Description}");
            }

            return ExitCodes.Clean;
        }
    }

    public static int Checks(ServiceProvider provider, TextWriter output)
    {
        using (provider)
        {
            var catalog = provider.GetRequiredService<ICheckCatalog>();

            foreach (var descriptor in catalog.Descriptors.OrderBy(d => d.Id, StringComparer.Ordinal))
            {
                output.WriteLine($"{descriptor.Id}  {descriptor.Category,-11} {descriptor.DefaultSeverity,-8} {descriptor.Title}");
                output.WriteLine($"  {descriptor.Rationale}");
            }

            return ExitCodes.Clean;
        }
    }
}
