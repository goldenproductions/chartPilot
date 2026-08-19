using Microsoft.AspNetCore.Diagnostics;

namespace ChartPilot.Api.Infrastructure;

/// <summary>
/// Turns every unhandled exception into a ProblemDetails response. A raw 500 with a stack trace is
/// never returned: this is a local tool, but the stack trace carries filesystem paths and adds
/// nothing the user can act on.
/// </summary>
public sealed class ChartPilotExceptionHandler : IExceptionHandler
{
    private readonly ILogger<ChartPilotExceptionHandler> _logger;

    public ChartPilotExceptionHandler(ILogger<ChartPilotExceptionHandler> logger) => _logger = logger;

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            // The GUI cancels in-flight renders on every keystroke; that is not an error.
            return true;
        }

        if (httpContext.Response.HasStarted)
        {
            return false;
        }

        var mapped = Problems.TryMap(exception);

        if (mapped is not null)
        {
            _logger.LogInformation(exception, "Request failed with a known condition: {Message}", exception.Message);
            await mapped.ExecuteAsync(httpContext).ConfigureAwait(false);
            return true;
        }

        _logger.LogError(exception, "Unhandled exception while handling {Path}", httpContext.Request.Path);

        var problem = Results.Problem(
            title: "Unexpected error",
            detail: exception.Message,
            statusCode: StatusCodes.Status500InternalServerError,
            type: "https://chartpilot.local/problems/unexpected");

        await problem.ExecuteAsync(httpContext).ConfigureAwait(false);

        return true;
    }
}
