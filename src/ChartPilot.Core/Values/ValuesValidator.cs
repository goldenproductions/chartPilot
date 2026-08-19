using System.Text;
using System.Text.Json;
using Json.Schema;

namespace ChartPilot.Core.Values;

/// <summary>
/// Validates values YAML on its own, and against a chart's <c>values.schema.json</c>.
/// Never throws: every failure mode — malformed YAML, a malformed schema, a schema violation —
/// comes back as an invalid <see cref="ValuesValidationResult"/>.
/// </summary>
public sealed class ValuesValidator : IValuesValidator
{
    private const string DocumentSource = "<values>";

    private static readonly EvaluationOptions Options = new()
    {
        OutputFormat = OutputFormat.List
    };

    /// <inheritdoc />
    public ValuesValidationResult ValidateYaml(string valuesYaml)
    {
        return TryParse(valuesYaml, out _, out var failure)
            ? Valid()
            : failure;
    }

    /// <inheritdoc />
    public ValuesValidationResult ValidateAgainstSchema(string valuesYaml, string schemaJson)
    {
        if (!TryParse(valuesYaml, out var document, out var parseFailure))
        {
            return parseFailure;
        }

        JsonSchema schema;
        try
        {
            schema = JsonSchema.FromText(schemaJson ?? string.Empty);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentNullException)
        {
            return Invalid(new ValuesValidationIssue(
                string.Empty,
                $"values.schema.json could not be parsed as a JSON Schema: {ex.Message}",
                "schema"));
        }

        var instance = YamlJsonConverter.ToJsonNode(document.Root);

        EvaluationResults results;
        try
        {
            results = schema.Evaluate(instance, Options);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            return Invalid(new ValuesValidationIssue(
                string.Empty,
                $"The values could not be evaluated against values.schema.json: {ex.Message}",
                "schema"));
        }

        if (results.IsValid)
        {
            return Valid();
        }

        var issues = new List<ValuesValidationIssue>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        Collect(results, issues, seen);

        if (issues.Count == 0)
        {
            issues.Add(new ValuesValidationIssue(
                string.Empty,
                "The values do not conform to values.schema.json.",
                null));
        }

        return new ValuesValidationResult(false, issues);
    }

    private static void Collect(EvaluationResults results, List<ValuesValidationIssue> issues, HashSet<string> seen)
    {
        if (results.HasErrors && results.Errors is { } errors)
        {
            var location = results.InstanceLocation;
            var path = ToDottedPath(location.ToString());

            foreach (var error in errors)
            {
                var keyword = string.IsNullOrWhiteSpace(error.Key) ? null : error.Key;
                var message = string.IsNullOrWhiteSpace(error.Value)
                    ? $"The value at '{(path.Length == 0 ? "(root)" : path)}' is not valid."
                    : error.Value!;

                if (seen.Add($"{path}{keyword}{message}"))
                {
                    issues.Add(new ValuesValidationIssue(path, message, keyword));
                }
            }
        }

        if (!results.HasDetails)
        {
            return;
        }

        foreach (var detail in results.Details)
        {
            Collect(detail, issues, seen);
        }
    }

    /// <summary>Turns a JSON Pointer such as <c>/image/tag</c> or <c>/hosts/0</c> into <c>image.tag</c> / <c>hosts[0]</c>.</summary>
    internal static string ToDottedPath(string? pointer)
    {
        if (string.IsNullOrEmpty(pointer) || pointer == "/")
        {
            return string.Empty;
        }

        var builder = new StringBuilder();

        foreach (var rawSegment in pointer.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            var segment = rawSegment
                .Replace("~1", "/", StringComparison.Ordinal)
                .Replace("~0", "~", StringComparison.Ordinal);

            if (segment.Length > 0 && segment.All(char.IsAsciiDigit))
            {
                builder.Append('[').Append(segment).Append(']');
                continue;
            }

            if (builder.Length > 0)
            {
                builder.Append('.');
            }

            builder.Append(segment);
        }

        return builder.ToString();
    }

    private static bool TryParse(string valuesYaml, out ValuesDocument document, out ValuesValidationResult failure)
    {
        try
        {
            document = ValuesDocument.Parse(valuesYaml ?? string.Empty, DocumentSource);
            failure = Valid();
            return true;
        }
        catch (ValuesParseException ex)
        {
            document = ValuesDocument.Empty(DocumentSource);
            failure = Invalid(new ValuesValidationIssue(string.Empty, Describe(ex), "yaml"));
            return false;
        }
    }

    private static string Describe(ValuesParseException exception)
    {
        if (exception.Line is null)
        {
            return exception.Message;
        }

        var position = exception.Column is null
            ? $"line {exception.Line}"
            : $"line {exception.Line}, column {exception.Column}";

        return $"Invalid YAML at {position}: {exception.Message}";
    }

    private static ValuesValidationResult Valid() => new(true, []);

    private static ValuesValidationResult Invalid(ValuesValidationIssue issue) => new(false, [issue]);
}
