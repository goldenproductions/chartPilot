using ChartPilot.Api.Contracts;
using ChartPilot.Api.Infrastructure;
using ChartPilot.Api.Workspaces;
using ChartPilot.Core.Charts;
using ChartPilot.Core.Values;
using ChartPilot.Helm;
using Microsoft.Extensions.Options;

namespace ChartPilot.Api.Endpoints;

/// <summary>Workspace lifecycle, the values editor and the N-way environment diff.</summary>
public static class WorkspaceEndpoints
{
    public static IEndpointRouteBuilder MapWorkspaceEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/workspaces", (
            CreateWorkspaceRequest? body,
            WorkspaceStore store,
            IChartLoader loader,
            IOptions<ChartPilotHelmOptions> helmOptions) =>
        {
            var chartPath = body?.ChartPath?.Trim();

            if (string.IsNullOrEmpty(chartPath))
            {
                return Problems.InvalidRequest("chartPath is required.");
            }

            // A relative path is resolved against the allowlist root, not the process working
            // directory: the GUI shows the root and lets the user type `samples/charts/member-api`.
            var allowlistRoot = helmOptions.Value.ResolveAllowlistRoot();

            string fullPath;

            try
            {
                fullPath = Path.IsPathRooted(chartPath)
                    ? Path.GetFullPath(chartPath)
                    : Path.GetFullPath(Path.Combine(allowlistRoot, chartPath));
            }
            catch (ArgumentException)
            {
                return Problems.InvalidRequest($"'{chartPath}' is not a valid path.");
            }

            if (!Directory.Exists(fullPath) || !loader.IsChartDirectory(fullPath))
            {
                return Problems.NotAChart(chartPath);
            }

            // Reject a chart outside the allowlist root here, where the message can be actionable,
            // rather than several requests later inside the Helm client.

            if (!PathGuard.IsUnder(allowlistRoot, fullPath))
            {
                return Problems.OutsideAllowlist(fullPath, allowlistRoot);
            }

            var model = loader.Load(fullPath);
            var workspace = store.Create(fullPath, model);

            workspace.SelectedValuesFiles = model.ValuesFiles
                .Where(v => v.IsDefault)
                .Select(v => v.FileName)
                .ToList();

            return Results.Created(
                $"{ApiEndpoints.BasePath}/workspaces/{workspace.Id}",
                DtoMapper.ToChartDto(workspace));
        })
        .WithName("CreateWorkspace");

        app.MapGet("/workspaces/{id}", (string id, WorkspaceStore store) =>
        {
            var workspace = store.Get(id);

            return workspace is null
                ? Problems.WorkspaceNotFound(id)
                : Results.Ok(DtoMapper.ToChartDto(workspace));
        })
        .WithName("GetWorkspace");

        app.MapGet("/workspaces/{id}/values", (
            string id,
            string? file,
            bool? draft,
            WorkspaceStore store) =>
        {
            var workspace = store.Get(id);

            if (workspace is null)
            {
                return Problems.WorkspaceNotFound(id);
            }

            var wantsDraft = draft ?? file is null;

            if (wantsDraft && workspace.DraftValuesYaml is { } yaml)
            {
                return Results.Ok(new ValuesDto("draft", yaml, IsDraft: true));
            }

            var fileName = file?.Trim() ?? WorkspaceFiles.DefaultFileName(workspace);

            if (fileName is null)
            {
                return Problems.InvalidRequest("The chart ships no values files.");
            }

            if (!WorkspaceFiles.TryResolve(workspace, fileName, out var fullPath))
            {
                return Problems.InvalidRequest(
                    $"'{fileName}' is not a values file of this chart. Known files: {string.Join(", ", WorkspaceFiles.Known(workspace))}.");
            }

            return Results.Ok(WorkspaceFiles.ReadDto(workspace, fileName, fullPath));
        })
        .WithName("GetWorkspaceValues");

        app.MapPut("/workspaces/{id}/values", (
            string id,
            UpdateValuesRequest? body,
            WorkspaceStore store,
            IValuesValidator validator) =>
        {
            var workspace = store.Get(id);

            if (workspace is null)
            {
                return Problems.WorkspaceNotFound(id);
            }

            if (body?.Yaml is null)
            {
                return Problems.InvalidRequest("yaml is required.");
            }

            // The draft is stored whether or not it validates: the user is mid-keystroke, and
            // throwing their text away because it does not parse yet would be hostile.
            workspace.DraftValuesYaml = body.Yaml;

            var result = workspace.ChartModel is { HasValuesSchema: true, ValuesSchemaJson: { } schema }
                ? validator.ValidateAgainstSchema(body.Yaml, schema)
                : validator.ValidateYaml(body.Yaml);

            return Results.Ok(new ValuesUpdateDto(
                Stored: true,
                IsValid: result.IsValid,
                Issues: DtoMapper.ToIssueDtos(result.Issues)));
        })
        .WithName("UpdateWorkspaceValues");

        // The one write-shaped operation the tool offers: hand the edited values back as a file the
        // user can drop into their repository. Nothing is written to the chart directory — the export
        // is a download, which is what architecture.md section 11 means by "an explicit export action".
        app.MapGet("/workspaces/{id}/values/export", (
            string id,
            string? file,
            WorkspaceStore store) =>
        {
            var workspace = store.Get(id);

            if (workspace is null)
            {
                return Problems.WorkspaceNotFound(id);
            }

            string yaml;
            string downloadName;

            if (file is { Length: > 0 })
            {
                var fileName = file.Trim();

                if (!WorkspaceFiles.TryResolve(workspace, fileName, out var fullPath))
                {
                    return Problems.InvalidRequest(
                        $"'{fileName}' is not a values file of this chart. Known files: {string.Join(", ", WorkspaceFiles.Known(workspace))}.");
                }

                yaml = File.ReadAllText(fullPath);
                downloadName = fileName;
            }
            else if (workspace.DraftValuesYaml is { } draft)
            {
                yaml = draft;
                downloadName = WorkspaceFiles.DefaultFileName(workspace) ?? "values.yaml";
            }
            else
            {
                var fallback = WorkspaceFiles.DefaultFileName(workspace);

                if (fallback is null || !WorkspaceFiles.TryResolve(workspace, fallback, out var fallbackPath))
                {
                    return Problems.InvalidRequest("The chart ships no values files and no draft has been edited.");
                }

                yaml = File.ReadAllText(fallbackPath);
                downloadName = fallback;
            }

            return Results.File(
                System.Text.Encoding.UTF8.GetBytes(yaml),
                "application/x-yaml",
                downloadName);
        })
        .WithName("ExportWorkspaceValues");

        app.MapGet("/workspaces/{id}/diff", (
            string id,
            string[]? files,
            bool? differencesOnly,
            bool? includeDraft,
            WorkspaceStore store,
            IValuesDiffService diffService) =>
        {
            var workspace = store.Get(id);

            if (workspace is null)
            {
                return Problems.WorkspaceNotFound(id);
            }

            IReadOnlyList<string> names = files is { Length: > 0 }
                ? files
                : WorkspaceFiles.Known(workspace);

            var documents = new List<ValuesDocument>();

            foreach (var name in names)
            {
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                if (!WorkspaceFiles.TryResolve(workspace, name.Trim(), out var fullPath))
                {
                    return Problems.InvalidRequest(
                        $"'{name.Trim()}' is not a values file of this chart. Known files: {string.Join(", ", WorkspaceFiles.Known(workspace))}.");
                }

                try
                {
                    documents.Add(ValuesDocument.Parse(File.ReadAllText(fullPath), name.Trim()));
                }
                catch (ValuesParseException ex)
                {
                    return Problems.InvalidRequest($"{name.Trim()}: {ex.Message}");
                }
            }

            if ((includeDraft ?? false) && workspace.DraftValuesYaml is { } draftYaml)
            {
                try
                {
                    documents.Add(ValuesDocument.Parse(draftYaml, "draft"));
                }
                catch (ValuesParseException ex)
                {
                    return Problems.InvalidRequest($"draft: {ex.Message}");
                }
            }

            if (documents.Count < 2)
            {
                return Problems.InvalidRequest("A diff needs at least two values documents.");
            }

            var diff = diffService.Diff(documents, differencesOnly ?? true);

            return Results.Ok(DtoMapper.ToDiffDto(diff));
        })
        .WithName("GetWorkspaceDiff");

        return app;
    }
}
