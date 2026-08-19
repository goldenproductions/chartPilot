using ChartPilot.Core.Charts;
using Microsoft.Extensions.Caching.Memory;

namespace ChartPilot.Api.Workspaces;

/// <summary>
/// Workspaces live in <see cref="IMemoryCache"/> with a sliding 30 minute TTL. Eviction deletes the
/// workspace's temp directory, so an abandoned browser tab does not leave draft values on disk.
/// </summary>
public sealed class WorkspaceStore
{
    /// <summary>Sliding expiration: a workspace stays alive as long as the tab keeps using it.</summary>
    public static readonly TimeSpan Ttl = TimeSpan.FromMinutes(30);

    private const string KeyPrefix = "chartpilot:workspace:";

    private readonly IMemoryCache _cache;
    private readonly TimeProvider _time;
    private readonly string _root;

    public WorkspaceStore(IMemoryCache cache, TimeProvider time)
    {
        _cache = cache;
        _time = time;
        _root = Path.Combine(Path.GetTempPath(), "chartpilot");
    }

    public Workspace Create(string chartPath, ChartModel model)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chartPath);
        ArgumentNullException.ThrowIfNull(model);

        var id = Guid.NewGuid().ToString("n");
        var tempDirectory = Path.Combine(_root, id);

        Directory.CreateDirectory(tempDirectory);

        var workspace = new Workspace
        {
            Id = id,
            ChartPath = Path.GetFullPath(chartPath),
            ChartModel = model,
            TempDirectory = tempDirectory,
            CreatedAt = _time.GetUtcNow()
        };

        var options = new MemoryCacheEntryOptions { SlidingExpiration = Ttl };
        options.RegisterPostEvictionCallback(static (_, value, _, _) => DeleteTempDirectory(value as Workspace));

        _cache.Set(KeyPrefix + id, workspace, options);

        return workspace;
    }

    public Workspace? Get(string id)
        => string.IsNullOrWhiteSpace(id) ? null : _cache.Get<Workspace>(KeyPrefix + id);

    public void Remove(string id)
    {
        if (!string.IsNullOrWhiteSpace(id))
        {
            _cache.Remove(KeyPrefix + id);
        }
    }

    private static void DeleteTempDirectory(Workspace? workspace)
    {
        if (workspace is null)
        {
            return;
        }

        try
        {
            if (Directory.Exists(workspace.TempDirectory))
            {
                Directory.Delete(workspace.TempDirectory, recursive: true);
            }
        }
        catch (IOException)
        {
            // A temp directory that is still locked is cleaned up by the OS; failing eviction is worse.
        }
        catch (UnauthorizedAccessException)
        {
            // Same: eviction must not throw on a background cache thread.
        }
    }
}
