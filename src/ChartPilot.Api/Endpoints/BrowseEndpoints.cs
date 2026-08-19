using ChartPilot.Api.Contracts;
using ChartPilot.Api.Infrastructure;
using ChartPilot.Core.Charts;
using ChartPilot.Helm;
using Microsoft.Extensions.Options;

namespace ChartPilot.Api.Endpoints;

/// <summary>
/// Directory listing for the GUI's "open a chart" browser.
///
/// A browser cannot hand a server an absolute path — neither a file input nor the File System
/// Access API exposes one — so the folder tree is walked server side instead. Every listing is
/// confined to the allowlist root, which makes this a navigation aid rather than a way to read
/// the filesystem: a caller can only see directory names it could already have typed.
/// </summary>
public static class BrowseEndpoints
{
    public static IEndpointRouteBuilder MapBrowseEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/browse", (
            string? path,
            IChartLoader loader,
            IOptions<ChartPilotHelmOptions> helmOptions) =>
        {
            var allowlistRoot = helmOptions.Value.ResolveAllowlistRoot();
            var requested = string.IsNullOrWhiteSpace(path) ? allowlistRoot : path.Trim();

            string fullPath;

            try
            {
                fullPath = PathGuard.NormalizeAgainst(allowlistRoot, requested);
            }
            catch (ArgumentException)
            {
                return Problems.InvalidRequest($"'{requested}' is not a valid path.");
            }

            // Containment is checked before existence, so a traversal attempt cannot be
            // distinguished from a missing directory by probing.
            if (!PathGuard.IsUnder(allowlistRoot, fullPath))
            {
                return Problems.OutsideAllowlist(fullPath, allowlistRoot);
            }

            if (!Directory.Exists(fullPath))
            {
                return Problems.DirectoryNotFound(requested);
            }

            return Results.Ok(Listing(fullPath, allowlistRoot, loader));
        })
        .WithName("BrowseDirectories");

        return app;
    }

    private static DirectoryListingDto Listing(string fullPath, string allowlistRoot, IChartLoader loader)
    {
        var isRoot = PathGuard.Normalize(fullPath).Equals(PathGuard.Normalize(allowlistRoot), PathComparison);

        return new DirectoryListingDto(
            Path: RelativeTo(allowlistRoot, fullPath),
            AbsolutePath: fullPath,
            AllowlistRoot: allowlistRoot,
            ParentPath: isRoot ? null : RelativeTo(allowlistRoot, Directory.GetParent(fullPath)!.FullName),
            IsAllowlistRoot: isRoot,
            IsChart: loader.IsChartDirectory(fullPath),
            Segments: Breadcrumbs(allowlistRoot, fullPath),
            Entries: Entries(fullPath, allowlistRoot, loader));
    }

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    private static IReadOnlyList<DirectoryEntryDto> Entries(string fullPath, string allowlistRoot, IChartLoader loader)
    {
        IEnumerable<string> directories;

        try
        {
            directories = Directory.EnumerateDirectories(fullPath);
        }
        catch (UnauthorizedAccessException)
        {
            // A directory the user cannot read lists as empty rather than failing the request:
            // one unreadable folder should not make its readable siblings unreachable.
            return [];
        }
        catch (IOException)
        {
            return [];
        }

        var entries = new List<DirectoryEntryDto>();

        foreach (var directory in directories)
        {
            DirectoryInfo info;

            try
            {
                info = new DirectoryInfo(directory);

                // Hidden and system directories are noise in a chart browser (.git, node_modules'
                // siblings, System Volume Information) and some of them cannot be read at all.
                if (info.Attributes.HasFlag(FileAttributes.Hidden) ||
                    info.Attributes.HasFlag(FileAttributes.System) ||
                    info.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    continue;
                }
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            entries.Add(new DirectoryEntryDto(
                Name: info.Name,
                Path: RelativeTo(allowlistRoot, info.FullName),
                IsChart: SafeIsChart(loader, info.FullName)));
        }

        return entries
            .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool SafeIsChart(IChartLoader loader, string path)
    {
        try
        {
            return loader.IsChartDirectory(path);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static IReadOnlyList<DirectorySegmentDto> Breadcrumbs(string allowlistRoot, string fullPath)
    {
        var segments = new List<DirectorySegmentDto>
        {
            new(Path.GetFileName(PathGuard.Normalize(allowlistRoot)) is { Length: > 0 } rootName
                    ? rootName
                    : allowlistRoot,
                string.Empty)
        };

        var relative = RelativeTo(allowlistRoot, fullPath);

        if (relative.Length == 0)
        {
            return segments;
        }

        var walked = string.Empty;

        foreach (var part in relative.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            walked = walked.Length == 0 ? part : $"{walked}/{part}";
            segments.Add(new DirectorySegmentDto(part, walked));
        }

        return segments;
    }

    /// <summary>
    /// Paths travel to the GUI relative to the allowlist root and with forward slashes, so they can
    /// be posted straight back to <c>POST /workspaces</c>, which resolves relative paths against
    /// that same root. The root itself is the empty string.
    /// </summary>
    private static string RelativeTo(string allowlistRoot, string fullPath)
    {
        var relative = Path.GetRelativePath(allowlistRoot, fullPath);

        return relative is "." ? string.Empty : relative.Replace('\\', '/');
    }
}
