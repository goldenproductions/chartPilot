using ChartPilot.Api.Contracts;
using ChartPilot.Api.Workspaces;
using ChartPilot.Core.Review;

namespace ChartPilot.Api.Endpoints;

/// <summary>
/// Values file handling for the workspace endpoints. A requested file name is only accepted when
/// the chart loader actually discovered it: the API never joins user input onto a path, so
/// <c>?file=../../etc/passwd</c> is a 400 rather than a read.
/// </summary>
internal static class WorkspaceFiles
{
    public static bool TryResolve(Workspace workspace, string fileName, out string fullPath)
    {
        foreach (var candidate in workspace.ChartModel.ValuesFiles)
        {
            if (string.Equals(candidate.FileName, fileName, StringComparison.OrdinalIgnoreCase))
            {
                fullPath = candidate.FullPath;
                return true;
            }
        }

        fullPath = string.Empty;
        return false;
    }

    public static IReadOnlyList<string> Known(Workspace workspace)
        => [.. workspace.ChartModel.ValuesFiles.Select(v => v.FileName)];

    public static string? DefaultFileName(Workspace workspace)
        => workspace.ChartModel.ValuesFiles.FirstOrDefault(v => v.IsDefault)?.FileName
           ?? workspace.ChartModel.ValuesFiles.FirstOrDefault()?.FileName;

    /// <summary>
    /// Keeps only the values files the chart actually ships, preserving the caller's order.
    /// An unknown name is a request error rather than a silently skipped layer.
    /// </summary>
    public static bool TryFilter(
        Workspace workspace,
        IReadOnlyList<string>? requested,
        out IReadOnlyList<string> accepted,
        out string? unknown)
    {
        unknown = null;

        if (requested is null || requested.Count == 0)
        {
            accepted = workspace.SelectedValuesFiles;
            return true;
        }

        var result = new List<string>(requested.Count);

        foreach (var name in requested)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            if (!TryResolve(workspace, name.Trim(), out _))
            {
                accepted = [];
                unknown = name.Trim();
                return false;
            }

            result.Add(name.Trim());
        }

        accepted = result;
        return true;
    }

    /// <summary>Builds the Core review request from the workspace state plus the request body.</summary>
    public static ReviewRequest ToReviewRequest(
        Workspace workspace,
        IReadOnlyList<string> valuesFiles,
        string? releaseName,
        string? profileId,
        string? environment,
        bool dependencyUpdate,
        bool runLint,
        string? draftValues = null)
        => new(
            workspace.ChartPath,
            string.IsNullOrWhiteSpace(releaseName) ? workspace.ChartModel.Name : releaseName.Trim(),
            valuesFiles,
            draftValues ?? workspace.DraftValuesYaml,
            profileId ?? string.Empty,
            string.IsNullOrWhiteSpace(environment) ? InferEnvironment(workspace, valuesFiles) : environment.Trim(),
            dependencyUpdate,
            runLint);

    /// <summary>
    /// The environment label defaults to the one implied by the last selected values file
    /// (values-prod.yaml means prod), because that is what the user actually picked in the GUI.
    /// </summary>
    private static string InferEnvironment(Workspace workspace, IReadOnlyList<string> valuesFiles)
    {
        for (var i = valuesFiles.Count - 1; i >= 0; i--)
        {
            var match = workspace.ChartModel.ValuesFiles
                .FirstOrDefault(v => string.Equals(v.FileName, valuesFiles[i], StringComparison.OrdinalIgnoreCase));

            if (match?.EnvironmentName is { Length: > 0 } environment)
            {
                return environment;
            }
        }

        return "default";
    }

    public static ValuesDto ReadDto(Workspace workspace, string fileName, string fullPath)
        => new(fileName, File.ReadAllText(fullPath), IsDraft: false);
}
