using System.Globalization;
using System.Text.Json.Nodes;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace ChartPilot.Core.Values;

/// <summary>
/// Converts the YamlDotNet representation model into <see cref="JsonNode"/> so that values documents
/// can be evaluated against a chart's <c>values.schema.json</c>.
/// </summary>
/// <remarks>
/// YAML has no type annotations on plain scalars, so types are inferred the way Helm's own
/// YAML-to-JSON conversion does: an explicitly quoted scalar is always a string, and a plain scalar
/// becomes null, bool, integer, floating point or string, in that order.
/// </remarks>
public static class YamlJsonConverter
{
    /// <summary>Converts a YAML node to its JSON equivalent. Never throws.</summary>
    public static JsonNode? ToJsonNode(YamlNode? node)
        => node switch
        {
            null => null,
            YamlMappingNode mapping => ToJsonObject(mapping),
            YamlSequenceNode sequence => ToJsonArray(sequence),
            YamlScalarNode scalar => ToJsonValue(scalar),
            _ => null
        };

    private static JsonObject ToJsonObject(YamlMappingNode mapping)
    {
        var result = new JsonObject();

        foreach (var entry in mapping.Children)
        {
            var key = entry.Key is YamlScalarNode { Value: { } scalarKey }
                ? scalarKey
                : entry.Key.ToString();

            if (string.IsNullOrEmpty(key))
            {
                continue;
            }

            // A duplicate key is invalid YAML but the representation model tolerates it; last one wins.
            result[key] = ToJsonNode(entry.Value);
        }

        return result;
    }

    private static JsonArray ToJsonArray(YamlSequenceNode sequence)
    {
        var result = new JsonArray();

        foreach (var child in sequence.Children)
        {
            result.Add(ToJsonNode(child));
        }

        return result;
    }

    private static JsonNode? ToJsonValue(YamlScalarNode scalar)
    {
        var value = scalar.Value;

        if (IsQuoted(scalar.Style))
        {
            return JsonValue.Create(value ?? string.Empty);
        }

        if (string.IsNullOrEmpty(value) ||
            value == "~" ||
            value.Equals("null", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (TryParseBoolean(value, out var boolean))
        {
            return JsonValue.Create(boolean);
        }

        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer))
        {
            return JsonValue.Create(integer);
        }

        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) &&
            !double.IsNaN(number) &&
            !double.IsInfinity(number))
        {
            return JsonValue.Create(number);
        }

        return JsonValue.Create(value);
    }

    private static bool IsQuoted(ScalarStyle style)
        => style is ScalarStyle.SingleQuoted or ScalarStyle.DoubleQuoted or ScalarStyle.Literal or ScalarStyle.Folded;

    private static bool TryParseBoolean(string value, out bool result)
    {
        switch (value.ToLowerInvariant())
        {
            case "true":
            case "yes":
            case "on":
                result = true;
                return true;
            case "false":
            case "no":
            case "off":
                result = false;
                return true;
            default:
                result = false;
                return false;
        }
    }
}
