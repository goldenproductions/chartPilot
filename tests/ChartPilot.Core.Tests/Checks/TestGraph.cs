using ChartPilot.Core.Manifests;
using YamlDotNet.RepresentationModel;

namespace ChartPilot.Core.Tests.Checks;

/// <summary>
/// An <see cref="IResourceGraph"/> over a list of <see cref="RenderedResource"/> parsed straight from
/// a fixture.
/// <para>
/// It delegates every query to the production <see cref="ResourceGraphBuilder"/>, so the check suite
/// exercises the real selector semantics and the real resolved edges. A hand-written second
/// implementation here would be a second definition of "does this policy select that pod" — the one
/// thing the graph exists to keep in a single place.
/// </para>
/// </summary>
public sealed class TestGraph : IResourceGraph
{
    private readonly IResourceGraph _inner;

    public TestGraph(IEnumerable<RenderedResource> resources)
    {
        _inner = new ResourceGraphBuilder().Build(resources.ToArray());
    }

    public IReadOnlyList<RenderedResource> Resources => _inner.Resources;

    public IReadOnlyList<GraphEdge> Edges => _inner.Edges;

    public IEnumerable<RenderedResource> ByKind(string kind) => _inner.ByKind(kind);

    public IEnumerable<RenderedResource> ByKinds(params string[] kinds) => _inner.ByKinds(kinds);

    public bool ContainsKind(string kind) => _inner.ContainsKind(kind);

    public RenderedResource? Find(string kind, string name) => _inner.Find(kind, name);

    public RenderedResource? Resolve(ResourceRef reference) => _inner.Resolve(reference);

    public IEnumerable<RenderedResource> Workloads() => _inner.Workloads();

    public IReadOnlyDictionary<string, string> PodLabelsOf(RenderedResource workload)
        => _inner.PodLabelsOf(workload);

    public IEnumerable<RenderedResource> SelectorMatches(string kind, IReadOnlyDictionary<string, string> podLabels)
        => _inner.SelectorMatches(kind, podLabels);

    public IEnumerable<RenderedResource> WorkloadsMatchedBySelector(IReadOnlyDictionary<string, string> selector)
        => _inner.WorkloadsMatchedBySelector(selector);

    public IReadOnlyList<GraphEdge> EdgesFrom(ResourceRef source) => _inner.EdgesFrom(source);

    public IReadOnlyList<GraphEdge> EdgesTo(ResourceRef target) => _inner.EdgesTo(target);

    // ------------------------------------------------------------------ construction

    /// <summary>Builds a graph from a multi-document manifest stream.</summary>
    public static TestGraph FromYaml(string multiDocumentYaml, string sourceTemplate = "fixture.yaml")
        => new(ParseResources(multiDocumentYaml, sourceTemplate));

    /// <summary>Builds a graph from a fixture file under Fixtures/Checks.</summary>
    public static TestGraph FromFixture(string relativePath)
    {
        var path = FixturePath(relativePath);
        return FromYaml(File.ReadAllText(path), Path.GetFileName(path));
    }

    /// <summary>The absolute path of a fixture below Fixtures/Checks.</summary>
    public static string FixturePath(string relativePath)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Checks", relativePath);

        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Fixture '{relativePath}' was not found at '{path}'.", path);
        }

        return path;
    }

    /// <summary>Parses a multi-document YAML stream into rendered resources, skipping empty documents.</summary>
    public static IReadOnlyList<RenderedResource> ParseResources(string multiDocumentYaml, string sourceTemplate)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(multiDocumentYaml));

        var resources = new List<RenderedResource>();

        foreach (var document in stream.Documents)
        {
            if (document.RootNode is not YamlMappingNode root)
            {
                continue;
            }

            var kind = ManifestNavigator.GetString(root, "kind");
            var name = ManifestNavigator.GetString(root, "metadata.name");

            if (string.IsNullOrEmpty(kind) || string.IsNullOrEmpty(name))
            {
                continue;
            }

            resources.Add(new RenderedResource(
                ManifestNavigator.GetString(root, "apiVersion") ?? "v1",
                kind,
                name,
                ManifestNavigator.GetString(root, "metadata.namespace"),
                sourceTemplate,
                root,
                Serialize(document)));
        }

        return resources;
    }

    private static string Serialize(YamlDocument document)
    {
        var stream = new YamlStream(document);
        using var writer = new StringWriter();
        stream.Save(writer, assignAnchors: false);
        return writer.ToString();
    }
}
