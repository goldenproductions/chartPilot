namespace ChartPilot.Helm;

/// <summary>A single child-process invocation. Arguments are passed as a list, never as one string.</summary>
public sealed record ProcessRequest(
    string FileName,
    IReadOnlyList<string> Arguments,
    string? WorkingDirectory,
    TimeSpan Timeout,
    int MaxOutputBytes)
{
    /// <summary>
    /// Environment variables to apply to the child process. A null value removes the variable,
    /// which is how the kubeconfig is neutralised for helm.
    /// </summary>
    public IReadOnlyDictionary<string, string?> EnvironmentOverrides { get; init; } =
        new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
}

/// <summary>The outcome of a child-process invocation.</summary>
public sealed record ProcessResult(
    int ExitCode,
    string StdOut,
    string StdErr,
    TimeSpan Duration,
    bool TimedOut,
    bool OutputTruncated);

/// <summary>
/// Indirection over <see cref="System.Diagnostics.Process"/>. It exists so that argument
/// construction, timeouts and truncation can be tested without helm installed.
/// </summary>
public interface IProcessRunner
{
    /// <summary>Runs the process to completion, a timeout, or caller cancellation.</summary>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken);
}
