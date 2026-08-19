using ChartPilot.Core.Manifests;

namespace ChartPilot.Core.Review;

/// <summary>
/// Everything needed to render and review a chart once. The same request drives the GUI, the CLI
/// and the report writer, so a finding produced in CI is the same finding the GUI shows.
/// </summary>
/// <param name="ChartPath">The chart directory (the one containing Chart.yaml).</param>
/// <param name="ReleaseName">The release name handed to <c>helm template</c>.</param>
/// <param name="ValuesFiles">Values files to layer, in order. Names are resolved relative to the chart directory.</param>
/// <param name="DraftValuesYaml">The in-memory editor draft, applied as the last layer.</param>
/// <param name="ProfileId">The golden path profile; an unknown id falls back to the store's default.</param>
/// <param name="Environment">The environment label carried into the checks and the report.</param>
/// <param name="DependencyUpdate">Opt in to <c>--dependency-update</c>, which hits the network.</param>
/// <param name="RunLint">Run <c>helm lint</c> and feed its messages into the check context.</param>
public sealed record ReviewRequest(
    string ChartPath,
    string ReleaseName,
    IReadOnlyList<string> ValuesFiles,
    string? DraftValuesYaml,
    string ProfileId,
    string Environment,
    bool DependencyUpdate = false,
    bool RunLint = true);

/// <summary>
/// The result of the render half of the pipeline: the parsed resources and the graph built over
/// them, or the reason Helm refused.
/// </summary>
/// <param name="RawManifests">The verbatim multi-document stream, for the manifest viewer.</param>
/// <param name="Error">A human-readable summary of the failure, or null on success.</param>
/// <param name="HelmStdErr">Helm's stderr verbatim, so the editor can point at the offending template line.</param>
public sealed record RenderOutcome(
    bool Success,
    IReadOnlyList<RenderedResource> Resources,
    IResourceGraph? Graph,
    string RawManifests,
    string? Error,
    string? HelmStdErr);

/// <summary>Renders a chart: values resolution, <c>helm template</c>, parsing, graph construction.</summary>
public interface IRenderService
{
    Task<RenderOutcome> RenderAsync(ReviewRequest request, CancellationToken ct = default);
}

/// <summary>The full pipeline: chart, values, render, checks, score.</summary>
public interface IReviewPipeline
{
    /// <exception cref="ReviewException">The chart could not be loaded or rendered.</exception>
    Task<ReviewResult> ReviewAsync(ReviewRequest request, CancellationToken ct = default);
}

/// <summary>
/// A review that could not be completed. A failed render surfaces as one of these carrying Helm's
/// stderr, never as a silently empty result.
/// </summary>
public sealed class ReviewException : Exception
{
    public ReviewException(string message, string? helmStdErr = null, string? source = null)
        : base(message)
    {
        HelmStdErr = helmStdErr;
        FailingSource = source;
    }

    /// <summary>Helm's stderr verbatim, when the failure came from Helm.</summary>
    public string? HelmStdErr { get; }

    /// <summary>The template (and line, when Helm reported one) the failure points at.</summary>
    public string? FailingSource { get; }
}
