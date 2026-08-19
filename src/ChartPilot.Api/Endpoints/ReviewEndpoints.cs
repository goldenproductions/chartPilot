using ChartPilot.Api.Contracts;
using ChartPilot.Api.Infrastructure;
using ChartPilot.Api.Workspaces;
using ChartPilot.Core.Checks;
using ChartPilot.Core.Reporting;
using ChartPilot.Core.Review;

namespace ChartPilot.Api.Endpoints;

/// <summary>
/// Render, review, report and workflow generation. Render and review are POSTs because they start a
/// process, and both honour <c>HttpContext.RequestAborted</c> — the GUI cancels the in-flight render
/// on every keystroke.
/// </summary>
public static class ReviewEndpoints
{
    public static IEndpointRouteBuilder MapReviewEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/workspaces/{id}/render", async (
            string id,
            RenderRequest? body,
            WorkspaceStore store,
            IRenderService renderService,
            CancellationToken ct) =>
        {
            var workspace = store.Get(id);

            if (workspace is null)
            {
                return Problems.WorkspaceNotFound(id);
            }

            if (!WorkspaceFiles.TryFilter(workspace, body?.ValuesFiles, out var valuesFiles, out var unknown))
            {
                return UnknownValuesFile(workspace, unknown);
            }

            workspace.SelectedValuesFiles = valuesFiles;

            var request = WorkspaceFiles.ToReviewRequest(
                workspace,
                valuesFiles,
                body?.ReleaseName,
                profileId: null,
                environment: null,
                dependencyUpdate: body?.DependencyUpdate ?? false,
                runLint: false);

            var outcome = await renderService.RenderAsync(request, ct).ConfigureAwait(false);

            if (!outcome.Success)
            {
                return Problems.ReviewFailed(new ReviewException(
                    outcome.Error ?? "helm template failed.",
                    outcome.HelmStdErr,
                    HelmErrorSource.Extract(outcome.HelmStdErr)));
            }

            workspace.LastRender = outcome;

            return Results.Ok(DtoMapper.ToRenderDto(workspace.Id, outcome));
        })
        .WithName("RenderWorkspace");

        app.MapPost("/workspaces/{id}/review", async (
            string id,
            ReviewRequestDto? body,
            WorkspaceStore store,
            IReviewPipeline pipeline,
            ICheckCatalog catalog,
            CancellationToken ct) =>
        {
            var workspace = store.Get(id);

            if (workspace is null)
            {
                return Problems.WorkspaceNotFound(id);
            }

            if (!WorkspaceFiles.TryFilter(workspace, body?.ValuesFiles, out var valuesFiles, out var unknown))
            {
                return UnknownValuesFile(workspace, unknown);
            }

            workspace.SelectedValuesFiles = valuesFiles;

            // A review that carries its own draft is the authority on what it reviewed; storing it
            // keeps the diff and the values export showing the same buffer.
            if (body?.DraftValues is not null)
            {
                workspace.DraftValuesYaml = body.DraftValues;
            }

            var result = await pipeline
                .ReviewAsync(BuildRequest(workspace, valuesFiles, body), ct)
                .ConfigureAwait(false);

            return Results.Ok(DtoMapper.ToReviewDto(workspace.Id, result, catalog));
        })
        .WithName("ReviewWorkspace");

        app.MapPost("/workspaces/{id}/report", async (
            string id,
            ReviewRequestDto? body,
            WorkspaceStore store,
            IReviewPipeline pipeline,
            IReportWriter writer,
            CancellationToken ct) =>
        {
            var workspace = store.Get(id);

            if (workspace is null)
            {
                return Problems.WorkspaceNotFound(id);
            }

            if (!WorkspaceFiles.TryFilter(workspace, body?.ValuesFiles, out var valuesFiles, out var unknown))
            {
                return UnknownValuesFile(workspace, unknown);
            }

            var result = await pipeline
                .ReviewAsync(BuildRequest(workspace, valuesFiles, body), ct)
                .ConfigureAwait(false);

            return Results.Text(writer.Write(result), "text/markdown", System.Text.Encoding.UTF8);
        })
        .WithName("ExportWorkspaceReport");

        app.MapPost("/workspaces/{id}/workflow", (
            string id,
            WorkflowRequest? body,
            WorkspaceStore store,
            IWorkflowGenerator generator) =>
        {
            var workspace = store.Get(id);

            if (workspace is null)
            {
                return Problems.WorkspaceNotFound(id);
            }

            var requested = body?.Environments;
            var environments = requested is { Count: > 0 } ? requested : DiscoverEnvironments(workspace);

            var chartPath = body?.ChartPath;
            var chartName = body?.ChartName;
            var ns = body?.Namespace;

            var options = new WorkflowOptions(
                string.IsNullOrWhiteSpace(chartPath) ? "./chart" : chartPath,
                string.IsNullOrWhiteSpace(chartName) ? workspace.ChartModel.Name : chartName,
                environments,
                body?.ProfileId ?? "default",
                body?.FailOn ?? "critical",
                string.IsNullOrWhiteSpace(ns) ? "default" : ns);

            return Results.Text(generator.Generate(options), "text/yaml", System.Text.Encoding.UTF8);
        })
        .WithName("GenerateWorkspaceWorkflow");

        return app;
    }

    private static ReviewRequest BuildRequest(
        Workspace workspace,
        IReadOnlyList<string> valuesFiles,
        ReviewRequestDto? body)
        => WorkspaceFiles.ToReviewRequest(
            workspace,
            valuesFiles,
            body?.ReleaseName,
            body?.ProfileId,
            body?.Environment,
            body?.DependencyUpdate ?? false,
            body?.RunLint ?? true,
            body?.DraftValues);

    private static IReadOnlyList<string> DiscoverEnvironments(Workspace workspace)
        => [.. workspace.ChartModel.ValuesFiles
            .Where(v => !string.IsNullOrEmpty(v.EnvironmentName))
            .Select(v => v.EnvironmentName!)
            .Distinct(StringComparer.OrdinalIgnoreCase)];

    private static IResult UnknownValuesFile(Workspace workspace, string? unknown)
        => Problems.InvalidRequest(
            $"'{unknown}' is not a values file of this chart. Known files: {string.Join(", ", WorkspaceFiles.Known(workspace))}.");
}
