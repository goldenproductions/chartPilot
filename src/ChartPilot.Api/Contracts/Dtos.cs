using ChartPilot.Core.Checks;
using ChartPilot.Core.Manifests;
using ChartPilot.Core.Profiles;

namespace ChartPilot.Api.Contracts;

// RenderedResource.Root is a YamlNode and is not serializable, so the API returns its own shapes
// rather than the Core records. Enums are serialized as strings (see Program.cs).

/// <summary>What GET /api/v1/environment reports: is Helm usable, and from where.</summary>
public sealed record EnvironmentDto(
    bool HelmAvailable,
    string? HelmPath,
    string? HelmVersion,
    string? HelmError,
    string ResolutionSource,
    string? AllowlistRoot,
    string ChartPilotVersion);

public sealed record ChartMaintainerDto(string Name, string? Email, string? Url);

public sealed record ChartDependencyDto(
    string Name,
    string? Version,
    string? Repository,
    string? Condition,
    IReadOnlyList<string> Tags,
    bool IsVersionPinned);

public sealed record ValuesFileDto(string FileName, string? EnvironmentName, bool IsDefault);

public sealed record TemplateFileDto(string RelativePath, IReadOnlyList<string> DetectedKinds);

/// <summary>A chart overview, plus the id of the workspace it was opened in.</summary>
public sealed record ChartDto(
    string WorkspaceId,
    string ChartPath,
    string Name,
    string Version,
    string? AppVersion,
    string? Description,
    string? Type,
    string? KubeVersion,
    IReadOnlyList<ChartMaintainerDto> Maintainers,
    IReadOnlyList<ChartDependencyDto> Dependencies,
    IReadOnlyList<ValuesFileDto> ValuesFiles,
    bool HasValuesSchema,
    string? ValuesSchemaJson,
    IReadOnlyList<TemplateFileDto> Templates,
    IReadOnlyList<string> DetectedKinds,
    bool HasSuppressionsFile,
    bool HasDraft,
    IReadOnlyList<string> SelectedValuesFiles,
    DateTimeOffset CreatedAt);

public sealed record ResourceDto(
    string ApiVersion,
    string ApiGroup,
    string Kind,
    string Name,
    string? Namespace,
    string SourceTemplate,
    ResourceCategory Category,
    string Yaml);

/// <summary>A finding, enriched with the catalog title and category so the GUI need not join.</summary>
public sealed record FindingDto(
    string CheckId,
    string? Title,
    CheckCategory? Category,
    Severity Severity,
    string? Resource,
    string? Kind,
    string? Name,
    string Message,
    string Remediation,
    string? YamlPath,
    string? SourceTemplate);

public sealed record PassedCheckDto(string CheckId, string Title, CheckCategory Category, string? Resource);

public sealed record SuppressedFindingDto(FindingDto Finding, string Reason, DateOnly? Expires);

public sealed record CategoryScoreDto(
    CheckCategory Category,
    int Score,
    int CriticalCount,
    int WarningCount,
    int InfoCount,
    int PassedCount);

public sealed record ScoreDto(int Overall, IReadOnlyList<CategoryScoreDto> Categories);

public sealed record ReviewDto(
    string WorkspaceId,
    string ChartName,
    string ChartVersion,
    string Environment,
    string ProfileId,
    DataClassification Classification,
    ScoreDto Score,
    int CriticalCount,
    int WarningCount,
    int InfoCount,
    IReadOnlyList<ResourceDto> Resources,
    IReadOnlyList<FindingDto> Findings,
    IReadOnlyList<PassedCheckDto> Passed,
    IReadOnlyList<SuppressedFindingDto> Suppressed,
    string? HelmVersion,
    DateTimeOffset GeneratedAt);

public sealed record RenderDto(
    string WorkspaceId,
    int ResourceCount,
    IReadOnlyList<ResourceDto> Resources,
    string RawManifests,
    string? HelmStdErr);

public sealed record ValuesDto(string Source, string Yaml, bool IsDraft);

public sealed record ValuesValidationIssueDto(string Path, string Message, string? Keyword);

/// <summary>
/// The result of PUT /values. The draft is stored whether or not it validates, so the user can keep
/// typing through a transient error.
/// </summary>
public sealed record ValuesUpdateDto(bool Stored, bool IsValid, IReadOnlyList<ValuesValidationIssueDto> Issues);

public sealed record DiffCellDto(string Source, string? Value, bool Present);

public sealed record DiffRowDto(string Path, IReadOnlyList<DiffCellDto> Cells, bool IsDifferent);

public sealed record DiffDto(IReadOnlyList<string> Sources, IReadOnlyList<DiffRowDto> Rows);

public sealed record ProfileDto(
    string Id,
    string Name,
    string Description,
    ProfileRequirements Requirements,
    IReadOnlyDictionary<string, Severity> SeverityOverrides,
    IReadOnlyList<string> DisabledChecks,
    ScoreWeights Weights,
    SeverityDeductions Deductions,
    bool IsDefault);

public sealed record CheckDto(
    string Id,
    string Title,
    CheckCategory Category,
    Severity DefaultSeverity,
    string Rationale,
    string Remediation,
    string? DocsUrl);

/// <summary>One subdirectory in the chart browser, and whether it is itself a chart.</summary>
public sealed record DirectoryEntryDto(string Name, string Path, bool IsChart);

/// <summary>One breadcrumb hop. <see cref="Path"/> is empty for the allowlist root.</summary>
public sealed record DirectorySegmentDto(string Name, string Path);

/// <summary>
/// What GET /api/v1/browse returns. Paths are relative to the allowlist root with forward
/// slashes, so the GUI can post one straight to POST /workspaces.
/// </summary>
public sealed record DirectoryListingDto(
    string Path,
    string AbsolutePath,
    string AllowlistRoot,
    string? ParentPath,
    bool IsAllowlistRoot,
    bool IsChart,
    IReadOnlyList<DirectorySegmentDto> Segments,
    IReadOnlyList<DirectoryEntryDto> Entries);
