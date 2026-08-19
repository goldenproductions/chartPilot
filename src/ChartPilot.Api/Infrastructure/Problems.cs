using ChartPilot.Core.Charts;
using ChartPilot.Core.Helm;
using ChartPilot.Core.Manifests;
using ChartPilot.Core.Values;
using ChartPilot.Core.Review;

namespace ChartPilot.Api.Infrastructure;

/// <summary>
/// The error vocabulary of the API. Every failure is a ProblemDetails; Helm's stderr travels in the
/// <c>helmStderr</c> extension and the offending template in <c>source</c>, which is what lets the
/// GUI point the editor at the line instead of showing a wall of text.
/// </summary>
public static class Problems
{
    /// <summary>Shown verbatim by the GUI banner when Helm is missing.</summary>
    public const string HelmInstallCommand = "winget install Helm.Helm";

    public const string HelmStdErrExtension = "helmStderr";
    public const string SourceExtension = "source";

    public static IResult WorkspaceNotFound(string id)
        => Results.Problem(
            title: "Workspace not found",
            detail: $"No workspace with id '{id}'. Workspaces expire after {WorkspaceTtlMinutes} minutes of inactivity; open the chart again.",
            statusCode: StatusCodes.Status404NotFound,
            type: "https://chartpilot.local/problems/workspace-not-found");

    public static IResult HelmUnavailable(string? reason = null)
        => Results.Problem(
            title: "Helm is not available",
            detail: string.IsNullOrWhiteSpace(reason)
                ? $"helm could not be located. Install it with: {HelmInstallCommand}"
                : $"{reason.Trim()} Install it with: {HelmInstallCommand}",
            statusCode: StatusCodes.Status503ServiceUnavailable,
            type: "https://chartpilot.local/problems/helm-unavailable",
            extensions: new Dictionary<string, object?>
            {
                ["installCommand"] = HelmInstallCommand
            });

    public static IResult ReviewFailed(ReviewException exception)
        => Results.Problem(
            title: "Review failed",
            detail: exception.Message,
            statusCode: StatusCodes.Status422UnprocessableEntity,
            type: "https://chartpilot.local/problems/review-failed",
            extensions: new Dictionary<string, object?>
            {
                [HelmStdErrExtension] = exception.HelmStdErr,
                [SourceExtension] = exception.FailingSource
            });

    public static IResult InvalidRequest(string detail)
        => Results.Problem(
            title: "Invalid request",
            detail: detail,
            statusCode: StatusCodes.Status400BadRequest,
            type: "https://chartpilot.local/problems/invalid-request");

    public static IResult OutsideAllowlist(string chartPath, string allowlistRoot)
        => Results.Problem(
            title: "Chart is outside the allowlist root",
            detail: $"'{chartPath}' is not under the allowlist root '{allowlistRoot}'. " +
                    $"ChartPilot only renders charts underneath that directory; set ChartPilot:AllowlistRoot " +
                    $"(or the {AllowlistRoots.EnvironmentVariable} environment variable) to widen it.",
            statusCode: StatusCodes.Status400BadRequest,
            type: "https://chartpilot.local/problems/outside-allowlist",
            extensions: new Dictionary<string, object?>
            {
                ["allowlistRoot"] = allowlistRoot
            });

    public static IResult NotAChart(string chartPath)
        => Results.Problem(
            title: "Not a chart directory",
            detail: $"No Chart.yaml was found in '{chartPath}'.",
            statusCode: StatusCodes.Status400BadRequest,
            type: "https://chartpilot.local/problems/not-a-chart");

    public static IResult DirectoryNotFound(string path)
        => Results.Problem(
            title: "Directory not found",
            detail: $"'{path}' does not exist.",
            statusCode: StatusCodes.Status404NotFound,
            type: "https://chartpilot.local/problems/directory-not-found");

    public static IResult UnknownEndpoint()
        => Results.Problem(
            title: "Unknown endpoint",
            detail: "No such API route.",
            statusCode: StatusCodes.Status404NotFound,
            type: "https://chartpilot.local/problems/unknown-endpoint");

    /// <summary>Kept in one place so the 404 message and the cache configuration cannot drift apart.</summary>
    internal static int WorkspaceTtlMinutes => (int)Workspaces.WorkspaceStore.Ttl.TotalMinutes;

    /// <summary>Strips the "(Parameter 'x')" suffix ArgumentException appends; the caller never saw that name.</summary>
    private static string WithoutParameterName(string message)
    {
        var marker = message.IndexOf(" (Parameter '", StringComparison.Ordinal);
        return marker > 0 ? message[..marker] : message;
    }

    /// <summary>A render whose output is not parseable YAML: bad input, not a server fault.</summary>
    public static IResult UnparseableManifests(string detail)
        => Results.Problem(
            title: "Rendered manifests could not be parsed",
            detail: detail,
            statusCode: StatusCodes.Status422UnprocessableEntity,
            type: "https://chartpilot.local/problems/unparseable-manifests");

    /// <summary>
    /// Maps the exceptions the pipeline can raise onto their problem responses. Everything the user
    /// can cause by pointing ChartPilot at something has an entry here: a path the guard refuses, a
    /// malformed chart, a malformed values file and a render that does not parse. Anything left over
    /// is a defect and becomes the generic 500 — which is exactly the distinction section 7 wants,
    /// because a path-traversal rejection presenting as an unexplained 500 teaches the user nothing.
    /// </summary>
    public static IResult? TryMap(Exception exception) => exception switch
    {
        HelmNotAvailableException helm => HelmUnavailable(helm.Message),
        ReviewException review => ReviewFailed(review),
        // A chart the user pointed at is input, not a server fault, so it is a 400 and not a 500.
        ChartLoadException chart => InvalidRequest(chart.Message),
        ValuesParseException values => InvalidRequest(values.Message),
        ManifestParseException manifests => UnparseableManifests(manifests.Message),
        // PathGuard refuses a values file outside the allowlist root with an ArgumentException.
        // ArgumentNullException is deliberately excluded: that one is always our own bug.
        ArgumentException argument when argument is not ArgumentNullException
            => InvalidRequest(WithoutParameterName(argument.Message)),
        _ => null
    };
}
