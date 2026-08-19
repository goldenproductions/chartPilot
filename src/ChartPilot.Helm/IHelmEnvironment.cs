namespace ChartPilot.Helm;

/// <summary>
/// The ambient machine state <see cref="HelmLocator"/> probes: environment variables,
/// file existence and directory globbing. Abstracted so the locator can be tested over a
/// temp directory tree without touching the real environment.
/// </summary>
public interface IHelmEnvironment
{
    string? GetEnvironmentVariable(string name);

    bool FileExists(string path);

    bool DirectoryExists(string path);

    /// <summary>Directories directly under <paramref name="path"/> matching a glob; empty when the parent is missing.</summary>
    IReadOnlyList<string> EnumerateDirectories(string path, string searchPattern);

    /// <summary>The user's home directory, used for <c>~/.local/bin</c> style locations.</summary>
    string HomeDirectory { get; }
}

/// <summary>The real machine.</summary>
public sealed class SystemHelmEnvironment : IHelmEnvironment
{
    public static readonly SystemHelmEnvironment Instance = new();

    public string? GetEnvironmentVariable(string name) => Environment.GetEnvironmentVariable(name);

    public bool FileExists(string path) => File.Exists(path);

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public IReadOnlyList<string> EnumerateDirectories(string path, string searchPattern)
    {
        try
        {
            return Directory.Exists(path)
                ? Directory.GetDirectories(path, searchPattern)
                : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    public string HomeDirectory
    {
        get
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrWhiteSpace(home))
            {
                return home;
            }

            return Environment.GetEnvironmentVariable("HOME") ?? string.Empty;
        }
    }
}
