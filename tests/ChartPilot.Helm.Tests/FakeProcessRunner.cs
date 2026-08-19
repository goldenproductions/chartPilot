using ChartPilot.Helm;

namespace ChartPilot.Helm.Tests;

/// <summary>
/// Records every <see cref="ProcessRequest"/> and returns canned output, so the whole helm
/// integration can be tested without helm installed.
/// </summary>
internal sealed class FakeProcessRunner : IProcessRunner
{
    private readonly List<ProcessRequest> _requests = [];

    public IReadOnlyList<ProcessRequest> Requests => _requests;

    public ProcessRequest LastRequest => _requests.Count > 0
        ? _requests[^1]
        : throw new InvalidOperationException("No process was started.");

    public int CallCount => _requests.Count;

    /// <summary>The result returned when <see cref="Handler"/> is not set.</summary>
    public ProcessResult Result { get; set; } = new(0, string.Empty, string.Empty, TimeSpan.FromMilliseconds(5), false, false);

    /// <summary>Optional per-call behaviour, e.g. to inspect files that exist only while helm runs.</summary>
    public Func<ProcessRequest, ProcessResult>? Handler { get; set; }

    public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _requests.Add(request);
        return Task.FromResult(Handler is null ? Result : Handler(request));
    }
}
