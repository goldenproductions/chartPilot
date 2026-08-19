using ChartPilot.Core.Io;

namespace ChartPilot.Core.Review;

/// <summary>
/// Turns the values file entries carried by a <see cref="ReviewRequest"/> into absolute paths.
/// Entries may be bare file names (<c>values-prod.yaml</c>), paths relative to the chart directory,
/// or absolute paths; anything that does not resolve to an existing file is reported rather than
/// silently dropped, because a values file that quietly did not apply is a wrong review.
/// <para>
/// Every probe goes through <see cref="IFileSystem"/>, so the layering rules are unit-testable
/// without a real directory on disk.
/// </para>
/// </summary>
internal sealed class ValuesFileResolver
{
    private readonly IFileSystem _fileSystem;

    public ValuesFileResolver(IFileSystem fileSystem)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    }

    public IReadOnlyList<string> Resolve(string chartPath, IReadOnlyList<string>? entries)
    {
        if (entries is null || entries.Count == 0)
        {
            return [];
        }

        var chartDirectory = NormalizeChartDirectory(chartPath);
        var resolved = new List<string>(entries.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var missing = new List<string>();

        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry))
            {
                continue;
            }

            var candidate = ResolveOne(chartDirectory, entry.Trim());

            if (candidate is null)
            {
                missing.Add(entry.Trim());
                continue;
            }

            if (seen.Add(candidate))
            {
                resolved.Add(candidate);
            }
        }

        if (missing.Count > 0)
        {
            throw new ReviewException(
                $"Values file not found: {string.Join(", ", missing)}");
        }

        return resolved;
    }

    /// <summary>The chart's own values.yaml, or null when the chart does not ship one.</summary>
    public string? DefaultValuesFile(string chartPath)
    {
        var candidate = Path.Combine(NormalizeChartDirectory(chartPath), "values.yaml");
        return _fileSystem.FileExists(candidate) ? _fileSystem.GetFullPath(candidate) : null;
    }

    public string NormalizeChartDirectory(string chartPath)
    {
        var full = _fileSystem.GetFullPath(chartPath);
        return _fileSystem.FileExists(full) ? Path.GetDirectoryName(full) ?? full : full;
    }

    private string? ResolveOne(string chartDirectory, string entry)
    {
        if (Path.IsPathRooted(entry))
        {
            return _fileSystem.FileExists(entry) ? _fileSystem.GetFullPath(entry) : null;
        }

        var relativeToChart = Path.Combine(chartDirectory, entry);

        if (_fileSystem.FileExists(relativeToChart))
        {
            return _fileSystem.GetFullPath(relativeToChart);
        }

        return _fileSystem.FileExists(entry) ? _fileSystem.GetFullPath(entry) : null;
    }
}
