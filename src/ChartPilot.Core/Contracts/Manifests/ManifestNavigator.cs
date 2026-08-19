using System.Globalization;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace ChartPilot.Core.Manifests;

/// <summary>
/// A container (or init container) inside a pod spec, together with the dotted YAML path
/// that locates it, i.e. <c>spec.template.spec.containers[0]</c>.
/// </summary>
public sealed record ContainerNode(string Name, YamlMappingNode Node, string YamlPath, bool IsInitContainer);

/// <summary>
/// Null-safe navigation over the YamlDotNet representation model. Used by the graph builder and by every check.
/// Nothing here throws on malformed or missing input; getters return <c>null</c> or an empty collection instead.
/// </summary>
public static class ManifestNavigator
{
    private static readonly IReadOnlyDictionary<string, string> EmptyMap =
        new Dictionary<string, string>(StringComparer.Ordinal);

    private static readonly IReadOnlyList<YamlNode> EmptySequence = Array.Empty<YamlNode>();

    /// <summary>
    /// Resolves a dotted path such as <c>spec.template.spec</c>. Sequence elements are addressed
    /// with <c>[i]</c> indexers, i.e. <c>spec.template.spec.containers[0].image</c>.
    /// An empty path returns <paramref name="node"/> itself.
    /// </summary>
    public static YamlNode? Get(YamlNode? node, string dottedPath)
    {
        if (node is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(dottedPath))
        {
            return node;
        }

        var current = node;

        foreach (var segment in dottedPath.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            var bracket = segment.IndexOf('[');
            var name = bracket >= 0 ? segment[..bracket] : segment;

            if (name.Length > 0)
            {
                if (current is not YamlMappingNode mapping)
                {
                    return null;
                }

                var child = Child(mapping, name);
                if (child is null)
                {
                    return null;
                }

                current = child;
            }

            if (bracket < 0)
            {
                continue;
            }

            var rest = segment[bracket..];
            var cursor = 0;

            while (cursor < rest.Length)
            {
                if (rest[cursor] != '[')
                {
                    return null;
                }

                var close = rest.IndexOf(']', cursor);
                if (close < 0)
                {
                    return null;
                }

                var raw = rest[(cursor + 1)..close];
                if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) || index < 0)
                {
                    return null;
                }

                if (current is not YamlSequenceNode sequence || index >= sequence.Children.Count)
                {
                    return null;
                }

                current = sequence.Children[index];
                cursor = close + 1;
            }
        }

        return current;
    }

    public static YamlMappingNode? AsMapping(YamlNode? node) => node as YamlMappingNode;

    public static YamlSequenceNode? AsSequence(YamlNode? node) => node as YamlSequenceNode;

    /// <summary>The scalar value at the path, or <c>null</c> when absent, non-scalar or an explicit YAML null.</summary>
    public static string? GetString(YamlNode? node, string dottedPath)
        => ScalarValue(Get(node, dottedPath));

    /// <summary>Accepts true/false, yes/no and on/off, case-insensitively.</summary>
    public static bool? GetBool(YamlNode? node, string dottedPath)
    {
        var value = GetString(node, dottedPath);
        if (value is null)
        {
            return null;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "true" or "yes" or "on" or "y" => true,
            "false" or "no" or "off" or "n" => false,
            _ => null
        };
    }

    public static int? GetInt(YamlNode? node, string dottedPath)
    {
        var value = GetString(node, dottedPath);
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    }

    /// <summary>Scalar-to-scalar mapping at the path, i.e. labels or annotations. Non-scalar entries are skipped.</summary>
    public static IReadOnlyDictionary<string, string> GetStringMap(YamlNode? node, string dottedPath)
    {
        if (Get(node, dottedPath) is not YamlMappingNode mapping)
        {
            return EmptyMap;
        }

        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var entry in mapping.Children)
        {
            if (entry.Key is YamlScalarNode { Value: { } key } && entry.Value is YamlScalarNode { Value: { } value })
            {
                result[key] = value;
            }
        }

        return result;
    }

    /// <summary>The sequence at the path, or an empty list when absent or not a sequence.</summary>
    public static IReadOnlyList<YamlNode> GetSequence(YamlNode? node, string dottedPath)
        => Get(node, dottedPath) is YamlSequenceNode sequence ? sequence.Children.ToList() : EmptySequence;

    /// <summary>
    /// The pod spec of a workload: <c>spec.template.spec</c>, <c>spec.jobTemplate.spec.template.spec</c>
    /// for a CronJob, or <c>spec</c> for a bare Pod.
    /// </summary>
    public static YamlMappingNode? GetPodSpec(RenderedResource resource)
        => resource is null ? null : AsMapping(Get(resource.Root, PodSpecPath(resource)));

    /// <summary>The dotted prefix of the pod spec for this resource, used to build YamlPath values.</summary>
    public static string PodSpecPath(RenderedResource resource)
    {
        if (resource is null)
        {
            return "spec";
        }

        return resource.Kind switch
        {
            "Pod" => "spec",
            "CronJob" => "spec.jobTemplate.spec.template.spec",
            _ => "spec.template.spec"
        };
    }

    /// <summary>Containers followed by init containers, each with the dotted path that locates it.</summary>
    public static IReadOnlyList<ContainerNode> GetContainers(RenderedResource resource)
    {
        var podSpec = GetPodSpec(resource);
        if (podSpec is null)
        {
            return Array.Empty<ContainerNode>();
        }

        var prefix = PodSpecPath(resource);
        var result = new List<ContainerNode>();

        Collect("containers", isInit: false);
        Collect("initContainers", isInit: true);

        return result;

        void Collect(string field, bool isInit)
        {
            if (Get(podSpec, field) is not YamlSequenceNode sequence)
            {
                return;
            }

            for (var i = 0; i < sequence.Children.Count; i++)
            {
                if (sequence.Children[i] is not YamlMappingNode container)
                {
                    continue;
                }

                var name = ScalarValue(Child(container, "name")) ?? string.Empty;
                result.Add(new ContainerNode(name, container, $"{prefix}.{field}[{i}]", isInit));
            }
        }
    }

    public static IReadOnlyDictionary<string, string> GetLabels(RenderedResource resource)
        => resource is null ? EmptyMap : GetStringMap(resource.Root, "metadata.labels");

    public static IReadOnlyDictionary<string, string> GetAnnotations(RenderedResource resource)
        => resource is null ? EmptyMap : GetStringMap(resource.Root, "metadata.annotations");

    private static YamlNode? Child(YamlMappingNode mapping, string key)
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

    private static string? ScalarValue(YamlNode? node)
    {
        if (node is not YamlScalarNode scalar)
        {
            return null;
        }

        if (scalar.Style != ScalarStyle.Plain)
        {
            return scalar.Value;
        }

        // A plain empty scalar (`key:`) or a plain `null` / `~` is a YAML null, not a value.
        if (string.IsNullOrEmpty(scalar.Value)
            || string.Equals(scalar.Value, "null", StringComparison.Ordinal)
            || string.Equals(scalar.Value, "~", StringComparison.Ordinal))
        {
            return null;
        }

        return scalar.Value;
    }
}
