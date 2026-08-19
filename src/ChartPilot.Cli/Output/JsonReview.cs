using System.Text.Json;
using System.Text.Json.Serialization;
using ChartPilot.Core.Checks;
using ChartPilot.Core.Manifests;
using ChartPilot.Core.Profiles;
using ChartPilot.Core.Review;

namespace ChartPilot.Cli.Output;

/// <summary>
/// The <c>--json</c> shape. <see cref="ReviewResult"/> itself cannot be serialized because a
/// rendered resource carries a YamlNode, so the CLI projects the parts a pipeline consumes.
/// </summary>
internal sealed record JsonReview(
    string Chart,
    string ChartVersion,
    string? AppVersion,
    string Environment,
    string ProfileId,
    DataClassification Classification,
    int Overall,
    IReadOnlyList<JsonCategoryScore> Categories,
    int CriticalCount,
    int WarningCount,
    int InfoCount,
    IReadOnlyList<JsonResource> Resources,
    IReadOnlyList<JsonFinding> Findings,
    IReadOnlyList<JsonPassed> Passed,
    IReadOnlyList<JsonSuppressed> Suppressed,
    string? HelmVersion,
    DateTimeOffset GeneratedAt);

internal sealed record JsonCategoryScore(
    CheckCategory Category,
    int Score,
    int CriticalCount,
    int WarningCount,
    int InfoCount,
    int PassedCount);

internal sealed record JsonResource(string Kind, string Name, string? Namespace, string SourceTemplate);

internal sealed record JsonFinding(
    string CheckId,
    Severity Severity,
    string? Resource,
    string Message,
    string Remediation,
    string? YamlPath,
    string? SourceTemplate);

internal sealed record JsonPassed(string CheckId, string Title, CheckCategory Category, string? Resource);

internal sealed record JsonSuppressed(string CheckId, string? Resource, string Reason, DateOnly? Expires);

internal static class JsonReviewMapper
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static JsonReview From(ReviewResult result)
        => new(
            result.Chart.Name,
            result.Chart.Version,
            result.Chart.AppVersion,
            result.Environment,
            result.ProfileId,
            result.Classification,
            result.Score.Overall,
            [.. result.Score.Categories.Select(c => new JsonCategoryScore(
                c.Category, c.Score, c.CriticalCount, c.WarningCount, c.InfoCount, c.PassedCount))],
            result.CriticalCount,
            result.WarningCount,
            result.InfoCount,
            [.. result.Resources.Select(ToResource)],
            [.. result.Findings.Select(ToFinding)],
            [.. result.Passed.Select(p => new JsonPassed(p.CheckId, p.Title, p.Category, p.Resource?.Key))],
            [.. result.Suppressed.Select(s => new JsonSuppressed(
                s.Finding.CheckId, s.Finding.Resource?.Key, s.Reason, s.Expires))],
            result.HelmVersion,
            result.GeneratedAt);

    private static JsonResource ToResource(RenderedResource resource)
        => new(resource.Kind, resource.Name, resource.Namespace, resource.SourceTemplate);

    private static JsonFinding ToFinding(Finding finding)
        => new(
            finding.CheckId,
            finding.Severity,
            finding.Resource?.Key,
            finding.Message,
            finding.Remediation,
            finding.YamlPath,
            finding.SourceTemplate);
}
