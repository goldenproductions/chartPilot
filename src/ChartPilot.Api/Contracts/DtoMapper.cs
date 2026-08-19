using ChartPilot.Api.Workspaces;
using ChartPilot.Core.Charts;
using ChartPilot.Core.Checks;
using ChartPilot.Core.Checks.Guidance;
using ChartPilot.Core.Manifests;
using ChartPilot.Core.Profiles;
using ChartPilot.Core.Review;
using ChartPilot.Core.Scoring;
using ChartPilot.Core.Values;

namespace ChartPilot.Api.Contracts;

/// <summary>Maps Core records onto the serializable API shapes.</summary>
public static class DtoMapper
{
    public static ChartDto ToChartDto(Workspace workspace)
    {
        var chart = workspace.ChartModel;

        return new ChartDto(
            workspace.Id,
            chart.ChartPath,
            chart.Name,
            chart.Version,
            chart.AppVersion,
            chart.Description,
            chart.Type,
            chart.KubeVersion,
            [.. chart.Maintainers.Select(m => new ChartMaintainerDto(m.Name, m.Email, m.Url))],
            [.. chart.Dependencies.Select(ToDependencyDto)],
            [.. chart.ValuesFiles.Select(v => new ValuesFileDto(v.FileName, v.EnvironmentName, v.IsDefault))],
            chart.HasValuesSchema,
            chart.ValuesSchemaJson,
            [.. chart.Templates.Select(t => new TemplateFileDto(t.RelativePath, t.DetectedKinds))],
            chart.DetectedKinds,
            chart.HasSuppressionsFile,
            !string.IsNullOrEmpty(workspace.DraftValuesYaml),
            workspace.SelectedValuesFiles,
            workspace.CreatedAt);
    }

    public static ChartDependencyDto ToDependencyDto(ChartDependency dependency)
        => new(
            dependency.Name,
            dependency.Version,
            dependency.Repository,
            dependency.Condition,
            dependency.Tags,
            dependency.IsVersionPinned);

    public static ResourceDto ToResourceDto(RenderedResource resource)
        => new(
            resource.ApiVersion,
            resource.ApiGroup,
            resource.Kind,
            resource.Name,
            resource.Namespace,
            resource.SourceTemplate,
            ResourceCategorizer.Categorize(resource.Kind),
            resource.Yaml);

    public static FindingDto ToFindingDto(Finding finding, ICheckCatalog catalog)
    {
        var descriptor = catalog.Find(finding.CheckId);
        var guidance = GuidanceCatalog.For(finding.CheckId);

        return new FindingDto(
            finding.CheckId,
            descriptor?.Title,
            descriptor?.Category,
            finding.Severity,
            finding.Resource?.Key,
            finding.Resource?.Kind,
            finding.Resource?.Name,
            finding.Message,
            finding.Remediation,
            finding.YamlPath,
            finding.SourceTemplate,
            descriptor?.Rationale,
            guidance?.WhatItMeans,
            finding.SeverityReason,
            guidance is null ? [] : [.. guidance.Options.Select(ToFixOptionDto)]);
    }

    private static FixOptionDto ToFixOptionDto(FixOption option)
        => new(option.Title, option.Summary, option.Yaml, option.Tradeoff, option.IsRecommended);

    public static PassedCheckDto ToPassedDto(PassedCheck passed)
        => new(passed.CheckId, passed.Title, passed.Category, passed.Resource?.Key);

    public static ScoreDto ToScoreDto(ScoreReport score)
        => new(
            score.Overall,
            [.. score.Categories.Select(c => new CategoryScoreDto(
                c.Category, c.Score, c.CriticalCount, c.WarningCount, c.InfoCount, c.PassedCount))]);

    public static ReviewDto ToReviewDto(string workspaceId, ReviewResult result, ICheckCatalog catalog)
        => new(
            workspaceId,
            result.Chart.Name,
            result.Chart.Version,
            result.Environment,
            result.ProfileId,
            result.Classification,
            ToScoreDto(result.Score),
            result.CriticalCount,
            result.WarningCount,
            result.InfoCount,
            [.. result.Resources.Select(ToResourceDto)],
            [.. result.Findings.Select(f => ToFindingDto(f, catalog))],
            [.. result.Passed.Select(ToPassedDto)],
            [.. result.Suppressed.Select(s => new SuppressedFindingDto(
                ToFindingDto(s.Finding, catalog), s.Reason, s.Expires))],
            result.HelmVersion,
            result.GeneratedAt);

    public static RenderDto ToRenderDto(string workspaceId, RenderOutcome outcome)
        => new(
            workspaceId,
            outcome.Resources.Count,
            [.. outcome.Resources.Select(ToResourceDto)],
            outcome.RawManifests,
            outcome.HelmStdErr);

    public static DiffDto ToDiffDto(ValuesDiffResult diff)
        => new(
            diff.Sources,
            [.. diff.Rows.Select(r => new DiffRowDto(
                r.Path,
                [.. r.Cells.Select(c => new DiffCellDto(c.Source, c.Value, c.Present))],
                r.IsDifferent))]);

    public static ProfileDto ToProfileDto(Profile profile, bool isDefault)
        => new(
            profile.Id,
            profile.Name,
            profile.Description,
            profile.Requirements,
            profile.SeverityOverrides,
            profile.DisabledChecks,
            profile.Weights,
            profile.Deductions,
            isDefault);

    public static CheckDto ToCheckDto(CheckDescriptor descriptor)
        => new(
            descriptor.Id,
            descriptor.Title,
            descriptor.Category,
            descriptor.DefaultSeverity,
            descriptor.Rationale,
            descriptor.Remediation,
            descriptor.DocsUrl);

    public static IReadOnlyList<ValuesValidationIssueDto> ToIssueDtos(IReadOnlyList<ValuesValidationIssue> issues)
        => [.. issues.Select(i => new ValuesValidationIssueDto(i.Path, i.Message, i.Keyword))];
}
