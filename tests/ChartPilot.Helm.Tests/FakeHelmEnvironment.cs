using ChartPilot.Helm;

namespace ChartPilot.Helm.Tests;

/// <summary>An in-test machine: a fixed environment block over a real temp directory tree.</summary>
internal sealed class FakeHelmEnvironment : IHelmEnvironment
{
    private readonly Dictionary<string, string> _variables = new(StringComparer.OrdinalIgnoreCase);

    public FakeHelmEnvironment(string? home = null)
    {
        HomeDirectory = home ?? string.Empty;
    }

    public string HomeDirectory { get; set; }

    /// <summary>
    /// When set, only paths underneath this directory are reported as existing. That keeps
    /// well-known-location tests deterministic on a machine that really has helm installed.
    /// </summary>
    public string? RestrictToRoot { get; set; }

    public FakeHelmEnvironment Set(string name, string? value)
    {
        if (value is null)
        {
            _variables.Remove(name);
        }
        else
        {
            _variables[name] = value;
        }

        return this;
    }

    public string? GetEnvironmentVariable(string name) =>
        _variables.TryGetValue(name, out var value) ? value : null;

    public bool FileExists(string path) => IsVisible(path) && File.Exists(path);

    public bool DirectoryExists(string path) => IsVisible(path) && Directory.Exists(path);

    public IReadOnlyList<string> EnumerateDirectories(string path, string searchPattern) =>
        IsVisible(path) && Directory.Exists(path) ? Directory.GetDirectories(path, searchPattern) : [];

    private bool IsVisible(string path)
    {
        if (string.IsNullOrEmpty(RestrictToRoot))
        {
            return true;
        }

        try
        {
            return ChartPilot.Helm.PathGuard.IsUnder(RestrictToRoot, path);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
