using ChartPilot.Core.Manifests;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace ChartPilot.Core.Values;

/// <summary>Thrown when a values document cannot be parsed as YAML, or is not a mapping at the root.</summary>
public sealed class ValuesParseException : Exception
{
    public ValuesParseException(string message, int? line, int? column)
        : base(message)
    {
        Line = line;
        Column = column;
    }

    /// <summary>1-based line of the offending token, when the parser reported one.</summary>
    public int? Line { get; }

    /// <summary>1-based column of the offending token, when the parser reported one.</summary>
    public int? Column { get; }
}

/// <summary>
/// A parsed Helm values document: the base <c>values.yaml</c>, an environment overlay,
/// or the in-memory draft the user is editing in the GUI.
/// </summary>
public sealed class ValuesDocument
{
    private IReadOnlyDictionary<string, string?>? _flattened;

    private ValuesDocument(string sourceName, string yaml, YamlMappingNode root)
    {
        SourceName = sourceName;
        Yaml = yaml;
        Root = root;
    }

    /// <summary>Where this document came from: <c>values.yaml</c>, <c>values-prod.yaml</c> or <c>&lt;draft&gt;</c>.</summary>
    public string SourceName { get; }

    /// <summary>The raw YAML text, exactly as supplied.</summary>
    public string Yaml { get; }

    /// <summary>The root mapping. Always a mapping, never null — an empty document yields an empty mapping.</summary>
    public YamlMappingNode Root { get; }

    /// <summary>
    /// Parses <paramref name="yaml"/>. Empty, whitespace-only and comment-only input yield an empty mapping.
    /// </summary>
    /// <exception cref="ValuesParseException">The YAML is malformed, or its root is not a mapping.</exception>
    public static ValuesDocument Parse(string yaml, string sourceName)
    {
        var text = yaml ?? string.Empty;
        var name = string.IsNullOrWhiteSpace(sourceName) ? "<values>" : sourceName;

        var stream = new YamlStream();

        try
        {
            using var reader = new StringReader(text);
            stream.Load(reader);
        }
        catch (YamlException ex)
        {
            throw new ValuesParseException(ex.Message, ToLineNumber(ex.Start.Line), ToLineNumber(ex.Start.Column));
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or FormatException)
        {
            // YamlDotNet's scanner does not always wrap malformed input in a YamlException
            // (an unterminated flow sequence, for example, surfaces as InvalidOperationException).
            throw new ValuesParseException($"is not valid YAML: {ex.Message}", null, null);
        }

        if (stream.Documents.Count == 0)
        {
            return new ValuesDocument(name, text, new YamlMappingNode());
        }

        var rootNode = stream.Documents[0].RootNode;

        return rootNode switch
        {
            YamlMappingNode mapping => new ValuesDocument(name, text, mapping),
            // `---` alone, or a document consisting only of a null scalar, is an empty values file.
            YamlScalarNode scalar when string.IsNullOrEmpty(scalar.Value) => new ValuesDocument(name, text, new YamlMappingNode()),
            _ => throw new ValuesParseException(
                $"The root of '{name}' must be a YAML mapping, but was {rootNode.NodeType}.",
                ToLineNumber(rootNode.Start.Line),
                ToLineNumber(rootNode.Start.Column))
        };
    }

    /// <summary>An empty document — used as the starting point of a draft or a missing overlay.</summary>
    public static ValuesDocument Empty(string sourceName)
        => new(string.IsNullOrWhiteSpace(sourceName) ? "<values>" : sourceName, string.Empty, new YamlMappingNode());

    /// <summary>Resolves a dotted path such as <c>image.tag</c> or <c>ingress.hosts[0].host</c>.</summary>
    public YamlNode? Get(string dottedPath) => ManifestNavigator.Get(Root, dottedPath);

    public string? GetString(string dottedPath) => ManifestNavigator.GetString(Root, dottedPath);

    public bool? GetBool(string dottedPath) => ManifestNavigator.GetBool(Root, dottedPath);

    public int? GetInt(string dottedPath) => ManifestNavigator.GetInt(Root, dottedPath);

    /// <summary>
    /// Every scalar leaf as dotted path to string value; sequence elements use <c>[i]</c> indexers.
    /// A YAML null leaf is present with a <c>null</c> value. The result is cached.
    /// </summary>
    public IReadOnlyDictionary<string, string?> Flatten()
    {
        if (_flattened is not null)
        {
            return _flattened;
        }

        var result = new Dictionary<string, string?>(StringComparer.Ordinal);
        Walk(Root, string.Empty, result);
        _flattened = result;
        return result;
    }

    /// <summary>YamlDotNet reports marks as long; findings and editors work in int line numbers.</summary>
    private static int? ToLineNumber(long value)
        => value <= 0 ? null : (int)Math.Min(value, int.MaxValue);

    private static void Walk(YamlNode node, string prefix, IDictionary<string, string?> sink)
    {
        switch (node)
        {
            case YamlMappingNode mapping:
                foreach (var entry in mapping.Children)
                {
                    if (entry.Key is not YamlScalarNode { Value: { } key })
                    {
                        continue;
                    }

                    var path = prefix.Length == 0 ? key : $"{prefix}.{key}";
                    Walk(entry.Value, path, sink);
                }

                break;

            case YamlSequenceNode sequence:
                for (var i = 0; i < sequence.Children.Count; i++)
                {
                    Walk(sequence.Children[i], $"{prefix}[{i}]", sink);
                }

                break;

            case YamlScalarNode:
                if (prefix.Length > 0)
                {
                    sink[prefix] = ManifestNavigator.GetString(node, string.Empty);
                }

                break;
        }
    }
}
