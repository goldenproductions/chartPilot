using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace ChartPilot.Core.Values;

/// <summary>
/// Merges values layers with Helm's own <c>-f a.yaml -f b.yaml</c> semantics.
/// </summary>
/// <remarks>
/// Mappings merge key by key and later layers win. Sequences and scalars are <em>replaced</em> wholesale —
/// Helm never concatenates lists, which is the single most common surprise when reading an overlay.
/// An explicit <c>null</c> in a later layer removes the key entirely, which is how Helm lets an
/// environment overlay switch a block off.
/// </remarks>
public sealed class ValuesMerger : IValuesMerger
{
    /// <inheritdoc />
    public ValuesDocument Merge(IReadOnlyList<ValuesDocument> layers, string resultSourceName)
    {
        ArgumentNullException.ThrowIfNull(layers);

        var name = string.IsNullOrWhiteSpace(resultSourceName) ? "<merged>" : resultSourceName;
        var result = new YamlMappingNode();

        foreach (var layer in layers)
        {
            if (layer is not null)
            {
                MergeInto(result, layer.Root);
            }
        }

        return ValuesDocument.Parse(Serialize(result), name);
    }

    private static void MergeInto(YamlMappingNode target, YamlMappingNode source)
    {
        foreach (var entry in source.Children)
        {
            if (entry.Key is not YamlScalarNode { Value: { } key })
            {
                continue;
            }

            if (IsExplicitNull(entry.Value))
            {
                Remove(target, key);
                continue;
            }

            if (entry.Value is YamlMappingNode sourceMapping)
            {
                if (Find(target, key) is YamlMappingNode targetMapping)
                {
                    MergeInto(targetMapping, sourceMapping);
                    continue;
                }

                var clone = new YamlMappingNode();
                MergeInto(clone, sourceMapping);
                Set(target, key, clone);
                continue;
            }

            Set(target, key, Clone(entry.Value));
        }
    }

    private static YamlNode? Find(YamlMappingNode mapping, string key)
    {
        foreach (var entry in mapping.Children)
        {
            if (entry.Key is YamlScalarNode scalar && string.Equals(scalar.Value, key, StringComparison.Ordinal))
            {
                return entry.Value;
            }
        }

        return null;
    }

    private static void Remove(YamlMappingNode mapping, string key)
    {
        var existing = mapping.Children
            .Where(e => e.Key is YamlScalarNode scalar && string.Equals(scalar.Value, key, StringComparison.Ordinal))
            .Select(e => e.Key)
            .ToList();

        foreach (var keyNode in existing)
        {
            mapping.Children.Remove(keyNode);
        }
    }

    /// <summary>Replaces the value for <paramref name="key"/>, preserving the key's original position.</summary>
    private static void Set(YamlMappingNode mapping, string key, YamlNode value)
    {
        YamlNode? existingKey = null;

        foreach (var entry in mapping.Children)
        {
            if (entry.Key is YamlScalarNode scalar && string.Equals(scalar.Value, key, StringComparison.Ordinal))
            {
                existingKey = entry.Key;
                break;
            }
        }

        if (existingKey is not null)
        {
            mapping.Children[existingKey] = value;
            return;
        }

        mapping.Children.Add(new YamlScalarNode(key), value);
    }

    private static bool IsExplicitNull(YamlNode node)
        => node is YamlScalarNode scalar
           && scalar.Style is ScalarStyle.Any or ScalarStyle.Plain
           && (string.IsNullOrEmpty(scalar.Value)
               || scalar.Value == "~"
               || string.Equals(scalar.Value, "null", StringComparison.Ordinal));

    private static YamlNode Clone(YamlNode node)
    {
        switch (node)
        {
            case YamlMappingNode mapping:
            {
                var clone = new YamlMappingNode();
                foreach (var entry in mapping.Children)
                {
                    clone.Children.Add(Clone(entry.Key), Clone(entry.Value));
                }

                return clone;
            }

            case YamlSequenceNode sequence:
            {
                var clone = new YamlSequenceNode();
                foreach (var child in sequence.Children)
                {
                    clone.Children.Add(Clone(child));
                }

                return clone;
            }

            case YamlScalarNode scalar:
                return new YamlScalarNode(scalar.Value) { Style = scalar.Style };

            default:
                return new YamlScalarNode(node.ToString());
        }
    }

    /// <summary>Emits the merged mapping as YAML text that parses back into the same structure.</summary>
    private static string Serialize(YamlMappingNode root)
    {
        if (root.Children.Count == 0)
        {
            return "{}" + Environment.NewLine;
        }

        using var writer = new StringWriter();
        var stream = new YamlStream(new YamlDocument(root));
        stream.Save(writer, assignAnchors: false);

        return Clean(writer.ToString());
    }

    /// <summary>Strips the document markers YamlDotNet emits so the result reads like a hand-written values file.</summary>
    private static string Clean(string yaml)
    {
        var text = yaml.Replace("\r\n", "\n", StringComparison.Ordinal);

        if (text.StartsWith("---\n", StringComparison.Ordinal))
        {
            text = text[4..];
        }

        var lines = text.Split('\n').ToList();

        while (lines.Count > 0 && lines[^1].Trim() is "" or "..." or "---")
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return lines.Count == 0
            ? "{}" + Environment.NewLine
            : string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }
}
