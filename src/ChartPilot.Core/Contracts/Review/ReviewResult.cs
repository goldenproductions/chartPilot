using ChartPilot.Core.Charts;
using ChartPilot.Core.Checks;
using ChartPilot.Core.Manifests;
using ChartPilot.Core.Profiles;
using ChartPilot.Core.Scoring;

namespace ChartPilot.Core.Review;

/// <summary>
/// The output of one full review: what was rendered, what was found, and what it scored.
/// The GUI JSON, the Markdown report and the CLI exit code are all functions of this record.
/// </summary>
/// <remarks>
/// <see cref="RenderedResource.Root"/> is a YamlNode and is not JSON-serializable, so the API layer
/// maps <see cref="Resources"/> onto its own DTOs rather than returning this record directly.
/// </remarks>
/// <param name="HelmVersion">The renderer that produced the manifests — a render is only reproducible alongside it.</param>
public sealed record ReviewResult(
    ChartModel Chart,
    string Environment,
    string ProfileId,
    DataClassification Classification,
    IReadOnlyList<RenderedResource> Resources,
    IReadOnlyList<Finding> Findings,
    IReadOnlyList<PassedCheck> Passed,
    IReadOnlyList<SuppressedFinding> Suppressed,
    ScoreReport Score,
    string? HelmVersion,
    DateTimeOffset GeneratedAt)
{
    public int CriticalCount => Count(Severity.Critical);

    public int WarningCount => Count(Severity.Warning);

    public int InfoCount => Count(Severity.Info);

    private int Count(Severity severity)
    {
        var total = 0;

        foreach (var finding in Findings)
        {
            if (finding.Severity == severity)
            {
                total++;
            }
        }

        return total;
    }
}
