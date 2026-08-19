using ChartPilot.Core.Charts;
using ChartPilot.Core.Review;

namespace ChartPilot.Api.Workspaces;

/// <summary>
/// One editing session: a chart directory, the in-memory draft of its values, and the last render.
/// Nothing here is persisted and nothing is written back to the chart directory — the temp
/// directory holds only the draft values file Helm is handed.
/// </summary>
public sealed class Workspace
{
    public required string Id { get; init; }

    /// <summary>The absolute chart directory. Read-only for the lifetime of the workspace.</summary>
    public required string ChartPath { get; init; }

    public required ChartModel ChartModel { get; set; }

    /// <summary>The editor's current draft, applied as the last values layer. Null until the user edits.</summary>
    public string? DraftValuesYaml { get; set; }

    /// <summary>The values files selected in the GUI, in layering order.</summary>
    public IReadOnlyList<string> SelectedValuesFiles { get; set; } = [];

    public RenderOutcome? LastRender { get; set; }

    /// <summary>Per-workspace scratch space, deleted when the workspace is evicted.</summary>
    public required string TempDirectory { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
}
