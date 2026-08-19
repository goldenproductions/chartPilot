using System.Text.RegularExpressions;
using ChartPilot.Core.Helm;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ChartPilot.Helm;

/// <summary>
/// Finds the helm binary, per architecture.md section 6.1: configured path, then PATH,
/// then the well-known install locations. A successful resolution (including the version read
/// from <c>helm version --short</c>) is cached for the lifetime of the process; a failed one is
/// not, so installing helm while ChartPilot is running is picked up on the next probe.
/// </summary>
public sealed partial class HelmLocator
{
    /// <summary>Resolution source reported on <see cref="HelmExecutable.ResolutionSource"/>.</summary>
    public const string SourceConfiguration = "configuration";

    public const string SourcePath = "path";
    public const string SourceWellKnown = "well-known";
    public const string SourceNotFound = "not-found";

    internal const string InstallHint =
        "helm was not found. Install it with 'winget install Helm.Helm' on Windows, " +
        "'brew install helm' on macOS, or set ChartPilot:HelmPath to the full path of the binary.";

    private static readonly TimeSpan VersionTimeout = TimeSpan.FromSeconds(10);

    private readonly IProcessRunner _runner;
    private readonly IOptions<ChartPilotHelmOptions> _options;
    private readonly IHelmEnvironment _environment;
    private readonly ILogger<HelmLocator> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private HelmExecutable? _cached;

    public HelmLocator(
        IProcessRunner runner,
        IOptions<ChartPilotHelmOptions> options,
        ILogger<HelmLocator>? logger = null,
        IHelmEnvironment? environment = null)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _environment = environment ?? SystemHelmEnvironment.Instance;
        _logger = logger ?? NullLogger<HelmLocator>.Instance;
    }

    /// <summary>Drops the cached resolution, forcing the next call to probe again.</summary>
    public void Invalidate()
    {
        _gate.Wait();
        try
        {
            _cached = null;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Locates helm and reads its version.</summary>
    public async Task<HelmExecutable> ResolveAsync(CancellationToken cancellationToken = default)
    {
        var cached = _cached;
        if (cached is not null)
        {
            return cached;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_cached is not null)
            {
                return _cached;
            }

            var resolved = await ProbeAsync(cancellationToken).ConfigureAwait(false);

            if (resolved.IsAvailable)
            {
                _cached = resolved;
            }

            return resolved;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<HelmExecutable> ProbeAsync(CancellationToken cancellationToken)
    {
        var candidate = FindCandidate();
        if (candidate is null)
        {
            _logger.LogWarning("helm binary could not be located.");
            return new HelmExecutable(false, null, null, InstallHint, SourceNotFound);
        }

        var (path, source) = candidate.Value;
        _logger.LogInformation("Resolved helm at {Path} via {Source}.", path, source);

        ProcessResult result;
        try
        {
            result = await _runner.RunAsync(
                new ProcessRequest(path, ["version", "--short"], null, VersionTimeout, 64 * 1024)
                {
                    EnvironmentOverrides = ClusterFreeEnvironment.Overrides
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return new HelmExecutable(
                false,
                path,
                null,
                $"Found helm at '{path}' but it could not be started: {ex.Message}",
                source);
        }

        if (result.TimedOut)
        {
            return new HelmExecutable(
                false,
                path,
                null,
                $"'{path} version --short' timed out after {VersionTimeout.TotalSeconds:0} seconds.",
                source);
        }

        if (result.ExitCode != 0)
        {
            var detail = string.IsNullOrWhiteSpace(result.StdErr) ? result.StdOut : result.StdErr;
            return new HelmExecutable(
                false,
                path,
                null,
                $"'{path} version --short' exited with code {result.ExitCode}. {detail.Trim()}".TrimEnd(),
                source);
        }

        var version = ParseVersion(result.StdOut) ?? ParseVersion(result.StdErr);
        return new HelmExecutable(true, path, version, null, source);
    }

    /// <summary>Extracts the semantic version out of output such as <c>v4.2.4+gabcdef0</c>.</summary>
    public static string? ParseVersion(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return null;
        }

        var match = VersionPattern().Match(output);
        return match.Success ? match.Groups["version"].Value : null;
    }

    private (string Path, string Source)? FindCandidate()
    {
        var configured = ResolveConfigured();
        if (configured is not null)
        {
            return (configured, SourceConfiguration);
        }

        var onPath = ResolveFromPath();
        if (onPath is not null)
        {
            return (onPath, SourcePath);
        }

        var wellKnown = ResolveWellKnown();
        return wellKnown is not null ? (wellKnown, SourceWellKnown) : null;
    }

    private string? ResolveConfigured()
    {
        var configured = _options.Value.HelmPath;
        if (string.IsNullOrWhiteSpace(configured))
        {
            return null;
        }

        var full = Path.GetFullPath(configured.Trim());

        if (_environment.FileExists(full))
        {
            return full;
        }

        if (_environment.DirectoryExists(full))
        {
            foreach (var name in ExecutableNames())
            {
                var combined = Path.Combine(full, name);
                if (_environment.FileExists(combined))
                {
                    return combined;
                }
            }
        }

        _logger.LogWarning("Configured helm path '{Path}' does not exist; falling back to PATH.", full);
        return null;
    }

    private string? ResolveFromPath()
    {
        var pathVariable = _environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathVariable))
        {
            return null;
        }

        var names = ExecutableNames();

        var entries = pathVariable.Split(
            Path.PathSeparator,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var entry in entries)
        {
            var directory = entry.Trim('"');
            if (directory.Length == 0)
            {
                continue;
            }

            foreach (var name in names)
            {
                string candidate;
                try
                {
                    candidate = Path.Combine(directory, name);
                }
                catch (ArgumentException)
                {
                    // A malformed PATH entry; skip it rather than failing resolution.
                    break;
                }

                if (_environment.FileExists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }
        }

        return null;
    }

    private string? ResolveWellKnown()
    {
        foreach (var candidate in WellKnownCandidates())
        {
            if (_environment.FileExists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        return null;
    }

    /// <summary>The well-known install locations, in probe order. Public for diagnostics and tests.</summary>
    public IReadOnlyList<string> WellKnownCandidates()
    {
        var candidates = new List<string>();

        var localAppData = _environment.GetEnvironmentVariable("LOCALAPPDATA");
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            var packages = Path.Combine(localAppData, "Microsoft", "WinGet", "Packages");
            var wingetDirectories = _environment.EnumerateDirectories(packages, "Helm.Helm_*")
                .OrderBy(static d => d, StringComparer.OrdinalIgnoreCase);

            foreach (var directory in wingetDirectories)
            {
                candidates.Add(Path.Combine(directory, "windows-amd64", "helm.exe"));
                candidates.Add(Path.Combine(directory, "windows-arm64", "helm.exe"));
                candidates.Add(Path.Combine(directory, "helm.exe"));
            }
        }

        var programFiles = _environment.GetEnvironmentVariable("ProgramFiles");
        if (!string.IsNullOrWhiteSpace(programFiles))
        {
            candidates.Add(Path.Combine(programFiles, "helm", "helm.exe"));
        }

        var programData = _environment.GetEnvironmentVariable("ProgramData");
        if (!string.IsNullOrWhiteSpace(programData))
        {
            candidates.Add(Path.Combine(programData, "chocolatey", "bin", "helm.exe"));
        }

        candidates.Add("/usr/local/bin/helm");

        var home = _environment.HomeDirectory;
        if (!string.IsNullOrWhiteSpace(home))
        {
            candidates.Add(Path.Combine(home, ".local", "bin", "helm"));
        }

        candidates.Add("/opt/homebrew/bin/helm");
        candidates.Add("/usr/bin/helm");

        return candidates;
    }

    private static string[] ExecutableNames() =>
        OperatingSystem.IsWindows() ? ["helm.exe", "helm"] : ["helm"];

    [GeneratedRegex(@"v?(?<version>\d+\.\d+(?:\.\d+)?(?:-[0-9A-Za-z.\-]+)?)", RegexOptions.CultureInvariant)]
    private static partial Regex VersionPattern();
}
