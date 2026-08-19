using System.Text;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace ChartPilot.Core.Manifests;

/// <summary>
/// Turns the multi-document YAML stream produced by <c>helm template</c> into <see cref="RenderedResource"/> records.
/// </summary>
public interface IManifestParser
{
    IReadOnlyList<RenderedResource> Parse(string multiDocumentYaml);
}

/// <summary>
/// Raised when the rendered manifest stream is not valid YAML. Carries the zero-based index of the offending
/// document inside the stream so the API can point the user at it.
/// </summary>
public sealed class ManifestParseException : Exception
{
    public ManifestParseException(string message, int documentIndex, int? line = null, Exception? innerException = null)
        : base(message, innerException)
    {
        DocumentIndex = documentIndex;
        Line = line;
    }

    /// <summary>Zero-based index of the document in the stream, or -1 when it could not be determined.</summary>
    public int DocumentIndex { get; }

    /// <summary>One-based line of the stream the parser failed on, when known.</summary>
    public int? Line { get; }
}

/// <summary>
/// The default <see cref="IManifestParser"/>. Documents are split by YamlDotNet (so a <c>---</c> inside a block
/// scalar or a quoted string never splits a document), and the <c># Source:</c> comment Helm writes above each
/// document is recovered by scanning the raw text alongside the parsed representation.
/// </summary>
public sealed class ManifestParser : IManifestParser
{
    /// <summary>
    /// Parses the stream. Empty and comment-only documents are skipped, documents without a kind or a name are
    /// skipped, and a document whose root is a Kubernetes <c>List</c> is expanded into its items.
    /// </summary>
    /// <exception cref="ManifestParseException">The stream is not valid YAML.</exception>
    public IReadOnlyList<RenderedResource> Parse(string multiDocumentYaml)
    {
        if (string.IsNullOrWhiteSpace(multiDocumentYaml))
        {
            return Array.Empty<RenderedResource>();
        }

        var text = Normalize(multiDocumentYaml);
        var lines = text.Split('\n');

        var stream = new YamlStream();
        try
        {
            stream.Load(new StringReader(text));
        }
        catch (YamlException ex)
        {
            var line = (int)Math.Max(ex.Start.Line, 0);
            var index = DocumentIndexOfLine(lines, line);
            throw new ManifestParseException(
                $"Failed to parse rendered manifest document {index} (line {line}): {ex.Message}",
                index,
                line > 0 ? line : null,
                ex);
        }
        catch (Exception ex)
        {
            throw new ManifestParseException($"Failed to parse the rendered manifest stream: {ex.Message}", -1, null, ex);
        }

        var documents = stream.Documents;
        var results = new List<RenderedResource>();

        for (var i = 0; i < documents.Count; i++)
        {
            var root = documents[i].RootNode;
            if (root is not YamlMappingNode mapping)
            {
                // Empty or comment-only document: legal Helm output, nothing to review.
                continue;
            }

            var startLine = (int)root.Start.Line;
            var endLineExclusive = i + 1 < documents.Count ? (int)documents[i + 1].RootNode.Start.Line : lines.Length + 1;

            var sourceTemplate = FindSourceComment(lines, startLine);
            var body = Slice(lines, startLine, endLineExclusive);

            AddDocument(results, mapping, sourceTemplate, body);
        }

        return results;
    }

    private static void AddDocument(List<RenderedResource> results, YamlMappingNode mapping, string sourceTemplate, string yaml)
    {
        var kind = ManifestNavigator.GetString(mapping, "kind");

        if (IsListWrapper(kind, mapping))
        {
            foreach (var item in ManifestNavigator.GetSequence(mapping, "items"))
            {
                if (item is YamlMappingNode itemMapping)
                {
                    AddDocument(results, itemMapping, sourceTemplate, Serialize(itemMapping));
                }
            }

            return;
        }

        if (string.IsNullOrEmpty(kind))
        {
            return;
        }

        var name = ManifestNavigator.GetString(mapping, "metadata.name");
        if (string.IsNullOrEmpty(name))
        {
            return;
        }

        var apiVersion = ManifestNavigator.GetString(mapping, "apiVersion") ?? string.Empty;
        var ns = ManifestNavigator.GetString(mapping, "metadata.namespace");
        if (string.IsNullOrWhiteSpace(ns))
        {
            ns = null;
        }

        results.Add(new RenderedResource(apiVersion, kind, name, ns, sourceTemplate, mapping, yaml));
    }

    private static bool IsListWrapper(string? kind, YamlMappingNode mapping)
        => string.Equals(kind, "List", StringComparison.Ordinal)
           && ManifestNavigator.Get(mapping, "items") is YamlSequenceNode;

    /// <summary>Re-emits a node that came out of a <c>kind: List</c> wrapper, so it displays like a normal document.</summary>
    private static string Serialize(YamlNode node)
    {
        var writer = new StringWriter { NewLine = "\n" };
        new YamlStream(new YamlDocument(node)).Save(writer, assignAnchors: false);

        var emitted = writer.ToString().Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var builder = new StringBuilder();

        foreach (var line in emitted)
        {
            if (line.StartsWith("---", StringComparison.Ordinal) || line.StartsWith("...", StringComparison.Ordinal))
            {
                continue;
            }

            builder.Append(line).Append('\n');
        }

        return builder.ToString().TrimEnd('\n');
    }

    /// <summary>
    /// The <c># Source:</c> comment immediately preceding the document, scanning back over the document separator
    /// and any other comment lines. Empty when Helm did not emit one.
    /// </summary>
    private static string FindSourceComment(string[] lines, int startLine)
    {
        for (var i = startLine - 2; i >= 0 && i < lines.Length; i--)
        {
            var line = lines[i].Trim();

            if (line.Length == 0 || line == "---" || line == "...")
            {
                continue;
            }

            if (!line.StartsWith("#", StringComparison.Ordinal))
            {
                return string.Empty;
            }

            var comment = line.TrimStart('#').Trim();
            if (comment.StartsWith("Source:", StringComparison.Ordinal))
            {
                return comment["Source:".Length..].Trim();
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// The raw text of one document: the lines from its first content line up to the next document, with the
    /// trailing separator, blank lines and top-level comments belonging to the next document removed.
    /// </summary>
    private static string Slice(string[] lines, int startLine, int endLineExclusive)
    {
        var first = Math.Max(startLine - 1, 0);
        var last = Math.Min(endLineExclusive - 2, lines.Length - 1);

        while (last >= first && IsTrailingNoise(lines[last]))
        {
            last--;
        }

        if (last < first || first >= lines.Length)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        for (var i = first; i <= last; i++)
        {
            if (i > first)
            {
                builder.Append('\n');
            }

            builder.Append(lines[i]);
        }

        return builder.ToString();
    }

    /// <summary>
    /// A line that belongs to the separator between two documents rather than to the document itself.
    /// Only column-zero separators and comments qualify, so an indented <c>---</c> inside a block scalar survives.
    /// </summary>
    private static bool IsTrailingNoise(string line)
    {
        if (line.Trim().Length == 0)
        {
            return true;
        }

        return line.StartsWith("---", StringComparison.Ordinal)
               || line.StartsWith("...", StringComparison.Ordinal)
               || line.StartsWith("#", StringComparison.Ordinal);
    }

    /// <summary>Best-effort mapping from a stream line back to a document index, used for parse errors.</summary>
    private static int DocumentIndexOfLine(string[] lines, int line)
    {
        if (line <= 0)
        {
            return -1;
        }

        var index = -1;
        var limit = Math.Min(line, lines.Length);

        for (var i = 0; i < limit; i++)
        {
            if (lines[i].StartsWith("---", StringComparison.Ordinal))
            {
                index++;
            }
        }

        return Math.Max(index, 0);
    }

    private static string Normalize(string text)
        => text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
}
