namespace ChartPilot.Api.Infrastructure;

/// <summary>
/// Decides which directory charts must live under. ChartPilot renders arbitrary Go templates, so
/// every chart and values path is forced under this root — but the root has to be wide enough to
/// contain the repositories a developer actually works in, or the tool is useless.
/// </summary>
public static class AllowlistRoots
{
    /// <summary>Environment variable escape hatch, so a user can widen or narrow the root without editing config.</summary>
    public const string EnvironmentVariable = "CHARTPILOT_ALLOWLIST_ROOT";

    /// <summary>
    /// Returns the configured root when one is set, otherwise the root of the checkout the API is
    /// running from (the directory holding <c>.git</c> or the solution file), otherwise the current
    /// working directory.
    /// </summary>
    public static string Resolve(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Path.GetFullPath(configured.Trim());
        }

        var fromEnvironment = Environment.GetEnvironmentVariable(EnvironmentVariable);

        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            return Path.GetFullPath(fromEnvironment.Trim());
        }

        return RepositoryRoot(AppContext.BaseDirectory)
               ?? RepositoryRoot(Directory.GetCurrentDirectory())
               ?? Path.GetFullPath(Directory.GetCurrentDirectory());
    }

    private static string? RepositoryRoot(string start)
    {
        var directory = new DirectoryInfo(start);

        for (var depth = 0; depth < 12 && directory is not null; depth++)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, ".git")) ||
                File.Exists(Path.Combine(directory.FullName, ".git")) ||
                Directory.EnumerateFiles(directory.FullName, "*.sln").Any())
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }
}
