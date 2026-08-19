using ChartPilot.Core.Io;
using ChartPilot.Core.Review;

namespace ChartPilot.Core.Tests.Review;

/// <summary>
/// The values-layering rules, exercised against an in-memory filesystem. These are pipeline
/// decisions — which file wins, what a bare name means, what happens to a name that resolves to
/// nothing — and they are only testable in isolation because Core reads through
/// <see cref="IFileSystem"/> rather than calling <c>File</c> directly.
/// </summary>
public sealed class ValuesFileResolverTests
{
    private const string ChartDirectory = "/charts/member-api";

    private static ValuesFileResolver Resolver(params string[] files)
        => new(new InMemoryFileSystem(files));

    [Fact]
    public void BareFileNames_ResolveAgainstTheChartDirectory()
    {
        var resolver = Resolver($"{ChartDirectory}/values.yaml", $"{ChartDirectory}/values-prod.yaml");

        var resolved = resolver.Resolve(ChartDirectory, ["values-prod.yaml"]);

        Assert.Equal([$"{ChartDirectory}/values-prod.yaml"], resolved);
    }

    [Fact]
    public void RepeatedEntries_AreLayeredOnlyOnce_AndKeepTheirOrder()
    {
        var resolver = Resolver($"{ChartDirectory}/a.yaml", $"{ChartDirectory}/b.yaml");

        var resolved = resolver.Resolve(ChartDirectory, ["b.yaml", "a.yaml", "b.yaml"]);

        Assert.Equal([$"{ChartDirectory}/b.yaml", $"{ChartDirectory}/a.yaml"], resolved);
    }

    [Fact]
    public void AnEntryThatResolvesToNothing_FailsTheReview()
    {
        var resolver = Resolver($"{ChartDirectory}/values.yaml");

        var exception = Assert.Throws<ReviewException>(
            () => resolver.Resolve(ChartDirectory, ["values-prod.yaml"]));

        Assert.Contains("values-prod.yaml", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DefaultValuesFile_IsNull_WhenTheChartShipsNone()
    {
        Assert.Null(Resolver($"{ChartDirectory}/Chart.yaml").DefaultValuesFile(ChartDirectory));
        Assert.Equal(
            $"{ChartDirectory}/values.yaml",
            Resolver($"{ChartDirectory}/values.yaml").DefaultValuesFile(ChartDirectory));
    }

    [Fact]
    public void APathToTheChartFileItself_NormalisesToItsDirectory()
    {
        var resolver = Resolver($"{ChartDirectory}/Chart.yaml");

        // Path.GetDirectoryName keeps the platform separator, which the fixture does not care about.
        static string Slashes(string path) => path.Replace('\\', '/');

        Assert.Equal(ChartDirectory, Slashes(resolver.NormalizeChartDirectory($"{ChartDirectory}/Chart.yaml")));
        Assert.Equal(ChartDirectory, Slashes(resolver.NormalizeChartDirectory(ChartDirectory)));
    }

    /// <summary>A filesystem that is a set of paths and nothing else.</summary>
    private sealed class InMemoryFileSystem : IFileSystem
    {
        private readonly HashSet<string> _files;

        public InMemoryFileSystem(IEnumerable<string> files)
        {
            _files = new HashSet<string>(files.Select(Normalize), StringComparer.OrdinalIgnoreCase);
        }

        public bool FileExists(string path) => _files.Contains(Normalize(path));

        public bool DirectoryExists(string path)
        {
            var prefix = Normalize(path) + "/";
            return _files.Any(file => file.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        }

        public string ReadAllText(string path)
            => FileExists(path) ? string.Empty : throw new FileNotFoundException(path);

        public string GetFullPath(string path) => Normalize(path);

        /// <summary>Keeps the test paths platform-independent: one separator, no trailing slash.</summary>
        private static string Normalize(string path)
            => path.Replace('\\', '/').TrimEnd('/');
    }
}
