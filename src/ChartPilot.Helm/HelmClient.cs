using System.Text.RegularExpressions;
using ChartPilot.Core.Helm;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ChartPilot.Helm;

/// <summary>
/// The only component in ChartPilot that starts a process. It renders and lints charts with
/// arguments built as a list (never a concatenated string), no kubeconfig, no <c>--dry-run</c>,
/// a wall-clock timeout, an output cap, and a traversal guard against the allowlist root.
/// </summary>
public sealed partial class HelmClient : IHelmClient
{
    private const string DraftValuesFileName = "draft-values.yaml";
    private const string TempRootName = "chartpilot";

    private readonly IProcessRunner _runner;
    private readonly HelmLocator _locator;
    private readonly IOptions<ChartPilotHelmOptions> _options;
    private readonly ILogger<HelmClient> _logger;

    public HelmClient(
        IProcessRunner runner,
        HelmLocator locator,
        IOptions<ChartPilotHelmOptions> options,
        ILogger<HelmClient>? logger = null)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _locator = locator ?? throw new ArgumentNullException(nameof(locator));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? NullLogger<HelmClient>.Instance;
    }

    /// <inheritdoc />
    public Task<HelmExecutable> ResolveAsync(CancellationToken cancellationToken = default) =>
        _locator.ResolveAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<HelmTemplateResult> TemplateAsync(HelmTemplateRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var options = _options.Value;
        var executablePath = await RequireHelmAsync(cancellationToken).ConfigureAwait(false);

        var allowlistRoot = options.ResolveAllowlistRoot();
        var chartPath = ResolveChartPath(allowlistRoot, request.ChartPath);
        var releaseName = ValidateReleaseName(options.ResolveReleaseName(request.ReleaseName));

        var arguments = new List<string> { "template", releaseName, chartPath };

        if (request.IncludeCrds)
        {
            arguments.Add("--include-crds");
        }

        if (request.SkipTests)
        {
            arguments.Add("--skip-tests");
        }

        if (request.DependencyUpdate)
        {
            arguments.Add("--dependency-update");
        }

        if (!string.IsNullOrWhiteSpace(request.KubeVersion))
        {
            arguments.Add("--kube-version");
            arguments.Add(RequireFlagValue(request.KubeVersion, nameof(request.KubeVersion)));
        }

        if (!string.IsNullOrWhiteSpace(request.Namespace))
        {
            arguments.Add("--namespace");
            arguments.Add(RequireFlagValue(request.Namespace, nameof(request.Namespace)));
        }

        foreach (var valuesFile in ResolveValuesFiles(allowlistRoot, chartPath, request.ValuesFiles ?? []))
        {
            arguments.Add("-f");
            arguments.Add(valuesFile);
        }

        string? tempDirectory = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(request.DraftValuesYaml))
            {
                tempDirectory = CreateTempDirectory();
                var draftPath = Path.Combine(tempDirectory, DraftValuesFileName);
                await File.WriteAllTextAsync(draftPath, request.DraftValuesYaml, cancellationToken).ConfigureAwait(false);

                // Last -f wins in helm, so the in-memory draft overrides every file on disk.
                arguments.Add("-f");
                arguments.Add(draftPath);
            }

            var result = await RunHelmAsync(
                executablePath,
                arguments,
                allowlistRoot,
                options.TemplateTimeout,
                options.MaxOutputBytes,
                cancellationToken).ConfigureAwait(false);

            var stdErr = result.TimedOut
                ? AppendTimeoutNote(result.StdErr, "helm template", options.TemplateTimeout)
                : result.StdErr;

            var success = !result.TimedOut && result.ExitCode == 0;

            if (!success)
            {
                _logger.LogWarning(
                    "helm template failed for {ChartPath} (exit {ExitCode}, timedOut {TimedOut}).",
                    chartPath,
                    result.ExitCode,
                    result.TimedOut);
            }

            return new HelmTemplateResult(
                success,
                result.StdOut,
                stdErr,
                result.ExitCode,
                result.Duration,
                result.TimedOut,
                result.OutputTruncated);
        }
        finally
        {
            DeleteTempDirectory(tempDirectory);
        }
    }

    /// <inheritdoc />
    public async Task<HelmLintResult> LintAsync(string chartPath, IReadOnlyList<string> valuesFiles, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chartPath);

        var options = _options.Value;
        var executablePath = await RequireHelmAsync(cancellationToken).ConfigureAwait(false);

        var allowlistRoot = options.ResolveAllowlistRoot();
        var resolvedChartPath = ResolveChartPath(allowlistRoot, chartPath);

        var arguments = new List<string> { "lint", resolvedChartPath };

        foreach (var valuesFile in ResolveValuesFiles(allowlistRoot, resolvedChartPath, valuesFiles ?? []))
        {
            arguments.Add("-f");
            arguments.Add(valuesFile);
        }

        var result = await RunHelmAsync(
            executablePath,
            arguments,
            allowlistRoot,
            options.LintTimeout,
            options.MaxOutputBytes,
            cancellationToken).ConfigureAwait(false);

        var messages = new List<HelmLintMessage>();
        messages.AddRange(HelmLintParser.Parse(result.StdOut, resolvedChartPath));
        messages.AddRange(HelmLintParser.Parse(result.StdErr, resolvedChartPath));

        var stdErr = result.TimedOut
            ? AppendTimeoutNote(result.StdErr, "helm lint", options.LintTimeout)
            : result.StdErr;

        return new HelmLintResult(
            !result.TimedOut && result.ExitCode == 0,
            messages,
            stdErr,
            result.ExitCode);
    }

    private async Task<string> RequireHelmAsync(CancellationToken cancellationToken)
    {
        var executable = await _locator.ResolveAsync(cancellationToken).ConfigureAwait(false);

        if (!executable.IsAvailable || string.IsNullOrWhiteSpace(executable.Path))
        {
            throw new HelmNotAvailableException(executable.Error ?? HelmLocator.InstallHint);
        }

        return executable.Path;
    }

    private async Task<ProcessResult> RunHelmAsync(
        string executablePath,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        TimeSpan timeout,
        int maxOutputBytes,
        CancellationToken cancellationToken)
    {
        var processRequest = new ProcessRequest(
            executablePath,
            arguments,
            Directory.Exists(workingDirectory) ? workingDirectory : null,
            timeout,
            maxOutputBytes)
        {
            // ChartPilot never contacts a cluster. Removing the kubeconfig variables means a chart
            // cannot reach an API server even if a template or a helm plugin tries.
            EnvironmentOverrides = ClusterFreeEnvironment.Overrides
        };

        try
        {
            return await _runner.RunAsync(processRequest, cancellationToken).ConfigureAwait(false);
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new HelmNotAvailableException($"helm at '{executablePath}' could not be started: {ex.Message}");
        }
    }

    private static string ResolveChartPath(string allowlistRoot, string chartPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chartPath);
        return PathGuard.EnsureUnder(allowlistRoot, allowlistRoot, chartPath, "Chart path");
    }

    private static IReadOnlyList<string> ResolveValuesFiles(
        string allowlistRoot,
        string chartPath,
        IReadOnlyList<string> valuesFiles)
    {
        if (valuesFiles.Count == 0)
        {
            return [];
        }

        var resolved = new List<string>(valuesFiles.Count);

        foreach (var valuesFile in valuesFiles)
        {
            if (string.IsNullOrWhiteSpace(valuesFile))
            {
                continue;
            }

            var trimmed = valuesFile.Trim();

            // A bare file name is most useful resolved against the chart directory; anything else
            // is resolved against the allowlist root, which is the user's working directory.
            var chartRelative = PathGuard.NormalizeAgainst(chartPath, trimmed);
            var candidate = !Path.IsPathRooted(trimmed) && File.Exists(chartRelative)
                ? chartRelative
                : PathGuard.NormalizeAgainst(allowlistRoot, trimmed);

            if (!PathGuard.IsUnder(allowlistRoot, candidate))
            {
                throw new ArgumentException(
                    $"Values file '{candidate}' resolves outside the allowlist root '{allowlistRoot}'.",
                    nameof(valuesFiles));
            }

            resolved.Add(candidate);
        }

        return resolved;
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), TempRootName, Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(path);
        return path;
    }

    private void DeleteTempDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not delete the temporary values directory {Path}.", path);
        }
    }

    private static string AppendTimeoutNote(string stdErr, string command, TimeSpan timeout)
    {
        var note = $"{command} timed out after {timeout.TotalSeconds:0.###} seconds and was terminated.";
        return string.IsNullOrWhiteSpace(stdErr) ? note : $"{stdErr.TrimEnd()}{Environment.NewLine}{note}";
    }

    private static string ValidateReleaseName(string releaseName)
    {
        if (!ReleaseNamePattern().IsMatch(releaseName))
        {
            throw new ArgumentException(
                $"'{releaseName}' is not a valid release name. Use letters, digits, '-', '_' and '.', starting and ending with a letter or digit.",
                nameof(releaseName));
        }

        return releaseName;
    }

    private static string RequireFlagValue(string? value, string name)
    {
        var trimmed = value!.Trim();
        if (trimmed.StartsWith('-'))
        {
            throw new ArgumentException($"{name} '{trimmed}' may not start with '-'.", name);
        }

        return trimmed;
    }

    [GeneratedRegex(@"^[A-Za-z0-9]([A-Za-z0-9._-]*[A-Za-z0-9])?$", RegexOptions.CultureInvariant)]
    private static partial Regex ReleaseNamePattern();
}
