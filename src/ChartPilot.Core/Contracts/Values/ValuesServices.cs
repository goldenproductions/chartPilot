namespace ChartPilot.Core.Values;

/// <summary>A single problem found while validating a values document.</summary>
/// <param name="Path">A dotted path, or the empty string for a document-level problem.</param>
/// <param name="Keyword">The JSON Schema keyword that failed, when the issue came from schema validation.</param>
public sealed record ValuesValidationIssue(string Path, string Message, string? Keyword);

/// <summary>The outcome of validating a values document.</summary>
public sealed record ValuesValidationResult(bool IsValid, IReadOnlyList<ValuesValidationIssue> Issues);

/// <summary>Validates values YAML, optionally against the chart's values.schema.json.</summary>
public interface IValuesValidator
{
    /// <summary>Syntax only: is this parseable YAML with a mapping root?</summary>
    ValuesValidationResult ValidateYaml(string valuesYaml);

    /// <summary>Syntax plus conformance to the supplied JSON Schema document.</summary>
    ValuesValidationResult ValidateAgainstSchema(string valuesYaml, string schemaJson);
}

/// <summary>Merges values layers the way Helm does.</summary>
public interface IValuesMerger
{
    /// <summary>
    /// Deep merge, later layers win. Mappings merge key by key; sequences and scalars are replaced
    /// wholesale, which is Helm's own <c>-f a.yaml -f b.yaml</c> semantics.
    /// </summary>
    ValuesDocument Merge(IReadOnlyList<ValuesDocument> layers, string resultSourceName);
}

/// <summary>One source's value for one path in an N-way diff.</summary>
/// <param name="Present">False when the path is absent from that source entirely.</param>
public sealed record ValuesDiffCell(string Source, string? Value, bool Present);

/// <summary>One path across every compared source.</summary>
public sealed record ValuesDiffRow(string Path, IReadOnlyList<ValuesDiffCell> Cells, bool IsDifferent);

/// <summary>An N-way structural comparison of values documents.</summary>
public sealed record ValuesDiffResult(IReadOnlyList<string> Sources, IReadOnlyList<ValuesDiffRow> Rows);

/// <summary>Compares two or more values documents path by path.</summary>
public interface IValuesDiffService
{
    /// <param name="differencesOnly">When true, rows where every source agrees are omitted.</param>
    ValuesDiffResult Diff(IReadOnlyList<ValuesDocument> documents, bool differencesOnly);
}
