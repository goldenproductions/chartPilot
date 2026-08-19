namespace ChartPilot.Api.Contracts;

/// <summary>POST /api/v1/workspaces — open a chart directory.</summary>
public sealed record CreateWorkspaceRequest(string? ChartPath);

/// <summary>PUT /api/v1/workspaces/{id}/values — replace the editor draft.</summary>
public sealed record UpdateValuesRequest(string? Yaml);

/// <summary>POST /api/v1/workspaces/{id}/render.</summary>
public sealed record RenderRequest(
    string? ReleaseName,
    IReadOnlyList<string>? ValuesFiles,
    bool? DependencyUpdate);

/// <summary>
/// POST /api/v1/workspaces/{id}/review and /report.
/// <para>
/// <c>DraftValues</c> makes a review self-contained: the caller states the exact editor buffer the
/// result belongs to instead of relying on a PUT that landed earlier. Without it a superseded PUT
/// and an in-flight review can disagree about which draft was reviewed. Null means "use the draft
/// stored on the workspace".
/// </para>
/// </summary>
public sealed record ReviewRequestDto(
    string? ReleaseName,
    IReadOnlyList<string>? ValuesFiles,
    string? ProfileId,
    string? Environment,
    bool? DependencyUpdate,
    bool? RunLint,
    string? DraftValues);

/// <summary>POST /api/v1/workspaces/{id}/workflow.</summary>
public sealed record WorkflowRequest(
    IReadOnlyList<string>? Environments,
    string? ProfileId,
    string? FailOn,
    string? Namespace,
    string? ChartPath,
    string? ChartName);
