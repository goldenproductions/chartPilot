using ChartPilot.Core.Helm;
using ChartPilot.Core.Io;
using ChartPilot.Core.Manifests;

namespace ChartPilot.Core.Review;

/// <summary>
/// The render half of the pipeline: resolve values files, run <c>helm template</c>, parse the
/// multi-document stream and build the resource graph over it.
/// </summary>
public sealed class RenderService : IRenderService
{
    private readonly IHelmClient _helm;
    private readonly IManifestParser _parser;
    private readonly IResourceGraphBuilder _graphBuilder;
    private readonly IFileSystem _fileSystem;
    private readonly ValuesFileResolver _valuesFiles;

    public RenderService(
        IHelmClient helm,
        IManifestParser parser,
        IResourceGraphBuilder graphBuilder,
        IFileSystem fileSystem)
    {
        _helm = helm;
        _parser = parser;
        _graphBuilder = graphBuilder;
        _fileSystem = fileSystem;
        _valuesFiles = new ValuesFileResolver(fileSystem);
    }

    public async Task<RenderOutcome> RenderAsync(ReviewRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var chartDirectory = _valuesFiles.NormalizeChartDirectory(request.ChartPath);

        if (!_fileSystem.DirectoryExists(chartDirectory))
        {
            throw new ReviewException($"Chart directory not found: {request.ChartPath}");
        }

        var valuesFiles = _valuesFiles.Resolve(chartDirectory, request.ValuesFiles);

        var templateRequest = new HelmTemplateRequest(
            ChartPath: chartDirectory,
            ReleaseName: string.IsNullOrWhiteSpace(request.ReleaseName) ? "release" : request.ReleaseName.Trim(),
            ValuesFiles: valuesFiles,
            DraftValuesYaml: string.IsNullOrWhiteSpace(request.DraftValuesYaml) ? null : request.DraftValuesYaml,
            IncludeCrds: true,
            SkipTests: true,
            DependencyUpdate: request.DependencyUpdate);

        var result = await _helm.TemplateAsync(templateRequest, ct).ConfigureAwait(false);

        if (!result.Success)
        {
            var error = result.TimedOut
                ? "helm template timed out."
                : $"helm template failed with exit code {result.ExitCode}.";

            return new RenderOutcome(false, [], null, result.Manifests, error, result.StdErr);
        }

        var resources = _parser.Parse(result.Manifests);
        var graph = _graphBuilder.Build(resources);

        return new RenderOutcome(
            true,
            resources,
            graph,
            result.Manifests,
            null,
            string.IsNullOrWhiteSpace(result.StdErr) ? null : result.StdErr);
    }
}
