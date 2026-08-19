namespace ChartPilot.Core.Values;

/// <summary>
/// Compares N values documents path by path — the "what actually differs between dev, test and prod"
/// view. Works for any number of documents, not just a pair.
/// </summary>
public sealed class ValuesDiffService : IValuesDiffService
{
    /// <inheritdoc />
    public ValuesDiffResult Diff(IReadOnlyList<ValuesDocument> documents, bool differencesOnly)
    {
        ArgumentNullException.ThrowIfNull(documents);

        var sources = new List<string>(documents.Count);
        var flattened = new List<IReadOnlyDictionary<string, string?>>(documents.Count);

        for (var i = 0; i < documents.Count; i++)
        {
            var document = documents[i];
            if (document is null)
            {
                continue;
            }

            sources.Add(string.IsNullOrWhiteSpace(document.SourceName) ? $"source-{i + 1}" : document.SourceName);
            flattened.Add(document.Flatten());
        }

        if (flattened.Count == 0)
        {
            return new ValuesDiffResult([], []);
        }

        var paths = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var values in flattened)
        {
            foreach (var path in values.Keys)
            {
                paths.Add(path);
            }
        }

        var rows = new List<ValuesDiffRow>(paths.Count);

        foreach (var path in paths)
        {
            var cells = new ValuesDiffCell[flattened.Count];

            for (var i = 0; i < flattened.Count; i++)
            {
                var present = flattened[i].TryGetValue(path, out var value);
                cells[i] = new ValuesDiffCell(sources[i], present ? value : null, present);
            }

            var isDifferent = IsDifferent(cells);

            if (isDifferent || !differencesOnly)
            {
                rows.Add(new ValuesDiffRow(path, cells, isDifferent));
            }
        }

        return new ValuesDiffResult(sources, rows);
    }

    /// <summary>A row differs when the sources disagree on the value, or on whether the path exists at all.</summary>
    private static bool IsDifferent(IReadOnlyList<ValuesDiffCell> cells)
    {
        if (cells.Count < 2)
        {
            return false;
        }

        var first = cells[0];

        for (var i = 1; i < cells.Count; i++)
        {
            if (cells[i].Present != first.Present ||
                !string.Equals(cells[i].Value, first.Value, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
