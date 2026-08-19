using ChartPilot.Core.Manifests;

namespace ChartPilot.Core.Tests.Manifests;

/// <summary>Loads the raw <c>helm template</c>-shaped fixtures and runs them through the real pipeline.</summary>
internal static class ManifestFixtures
{
    private static readonly ManifestParser Parser = new();
    private static readonly ResourceGraphBuilder Builder = new();

    internal static string Read(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Manifests", fileName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Manifest fixture '{fileName}' was not copied to the test output.", path);
        }

        return File.ReadAllText(path);
    }

    internal static IReadOnlyList<RenderedResource> Parse(string fileName) => Parser.Parse(Read(fileName));

    internal static IReadOnlyList<RenderedResource> ParseText(string yaml) => Parser.Parse(yaml);

    internal static IResourceGraph Graph(string fileName) => Builder.Build(Parse(fileName));

    internal static IResourceGraph GraphOfText(string yaml) => Builder.Build(ParseText(yaml));

    /// <summary>The edge targets of one relation leaving a resource, as <c>Kind/Name</c> keys.</summary>
    internal static IReadOnlyList<string> TargetsOf(IResourceGraph graph, string kind, string name, string relation)
        => graph.EdgesFrom(new ResourceRef(kind, name))
            .Where(e => e.Relation == relation)
            .Select(e => e.To.Key)
            .ToList();
}
