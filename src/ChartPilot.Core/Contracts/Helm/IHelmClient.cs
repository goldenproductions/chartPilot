namespace ChartPilot.Core.Helm;

/// <summary>The result of locating the helm binary.</summary>
/// <param name="ResolutionSource">How it was found: configuration, path, well-known-location, or not-found.</param>
public sealed record HelmExecutable(bool IsAvailable, string? Path, string? Version, string? Error, string ResolutionSource);

/// <summary>
/// A request to render a chart. Draft values are written to a temp file by the client;
/// user input is never concatenated into process arguments.
/// </summary>
public sealed record HelmTemplateRequest(
    string ChartPath,
    string ReleaseName,
    IReadOnlyList<string> ValuesFiles,
    string? DraftValuesYaml = null,
    bool IncludeCrds = true,
    bool SkipTests = true,
    bool DependencyUpdate = false,
    string? KubeVersion = null,
    string? Namespace = null);

/// <summary>The result of a render. Helm's stderr is preserved verbatim for the GUI error panel.</summary>
public sealed record HelmTemplateResult(
    bool Success,
    string Manifests,
    string StdErr,
    int ExitCode,
    TimeSpan Duration,
    bool TimedOut,
    bool OutputTruncated);

/// <summary>Severity of a single helm lint line.</summary>
public enum HelmLintSeverity
{
    Info,
    Warning,
    Error
}

/// <summary>One parsed line of helm lint output: [WARNING] templates/deployment.yaml: message.</summary>
public sealed record HelmLintMessage(HelmLintSeverity Severity, string File, string Message);

/// <summary>The result of a lint run.</summary>
public sealed record HelmLintResult(bool Success, IReadOnlyList<HelmLintMessage> Messages, string StdErr, int ExitCode);

/// <summary>
/// The only part of ChartPilot that starts a process. No kubeconfig is passed and no cluster is
/// contacted, so a chart cannot reach an API server even if its templates try.
/// </summary>
public interface IHelmClient
{
    /// <summary>Locates the helm binary and reads its version.</summary>
    Task<HelmExecutable> ResolveAsync(CancellationToken cancellationToken = default);

    /// <summary>Runs helm template. Never throws for a chart-authoring error; that is reported through the result.</summary>
    /// <exception cref="HelmNotAvailableException">helm could not be located.</exception>
    Task<HelmTemplateResult> TemplateAsync(HelmTemplateRequest request, CancellationToken cancellationToken = default);

    /// <summary>Runs helm lint; its messages are folded into the findings list under CP-GOV-*.</summary>
    /// <exception cref="HelmNotAvailableException">helm could not be located.</exception>
    Task<HelmLintResult> LintAsync(string chartPath, IReadOnlyList<string> valuesFiles, CancellationToken cancellationToken = default);
}

/// <summary>Thrown when an operation needs helm and the binary could not be located.</summary>
public sealed class HelmNotAvailableException(string message) : Exception(message);
