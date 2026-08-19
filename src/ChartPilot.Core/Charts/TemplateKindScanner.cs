using System.Text.RegularExpressions;

namespace ChartPilot.Core.Charts;

/// <summary>
/// A deliberately approximate, purely textual scan for the Kubernetes kinds a template can emit.
/// </summary>
/// <remarks>
/// Templates are Go templates, so they are usually not valid YAML before rendering — parsing them
/// is not an option. This scanner therefore works line by line and is written so that it can never
/// throw on template syntax: the worst case is that it reports no kinds for a file.
/// <para>
/// The heuristics are:
/// <list type="bullet">
/// <item>text inside <c>{{ ... }}</c> is removed before matching, so a <c>kind:</c> mentioned inside a
/// template expression never counts;</item>
/// <item>comment lines (<c>#</c> or <c>{{/* ... */}}</c>) are ignored;</item>
/// <item>only <c>kind:</c> at the very start of a line counts, which excludes nested uses such as an
/// RBAC <c>roleRef.kind</c> or a subject's <c>kind</c>;</item>
/// <item>a document's kinds only count when the same document also has a top-level
/// <c>apiVersion:</c> — the sanity check that keeps arbitrary data files out of the list.</item>
/// </list>
/// </para>
/// </remarks>
public static class TemplateKindScanner
{
    private static readonly Regex TemplateExpression = new(
        @"\{\{.*?\}\}",
        RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.CultureInvariant);

    private static readonly Regex TopLevelKind = new(
        @"^kind:[ \t]*(?<value>.*)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex TopLevelApiVersion = new(
        @"^apiVersion:",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex KindValue = new(
        @"^[A-Za-z][A-Za-z0-9]*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Scans template text. Never throws; returns a distinct, ordinally sorted list.</summary>
    public static IReadOnlyList<string> Scan(string? templateText)
    {
        if (string.IsNullOrWhiteSpace(templateText))
        {
            return [];
        }

        var kinds = new SortedSet<string>(StringComparer.Ordinal);
        var documentKinds = new List<string>();
        var documentHasApiVersion = false;

        foreach (var rawLine in SplitLines(templateText))
        {
            var line = rawLine;

            if (IsDocumentSeparator(line))
            {
                Flush(kinds, documentKinds, documentHasApiVersion);
                documentHasApiVersion = false;
                continue;
            }

            line = StripTemplateExpressions(line);

            if (line.Length == 0 || IsComment(line))
            {
                continue;
            }

            if (TopLevelApiVersion.IsMatch(line))
            {
                documentHasApiVersion = true;
                continue;
            }

            var match = TopLevelKind.Match(line);
            if (!match.Success)
            {
                continue;
            }

            var value = CleanKind(match.Groups["value"].Value);
            if (value is not null)
            {
                documentKinds.Add(value);
            }
        }

        Flush(kinds, documentKinds, documentHasApiVersion);

        return kinds.ToArray();
    }

    /// <summary>Scans a template file on disk. An unreadable file yields an empty list rather than an exception.</summary>
    public static IReadOnlyList<string> ScanFile(string path)
    {
        try
        {
            return Scan(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return [];
        }
    }

    private static void Flush(SortedSet<string> sink, List<string> documentKinds, bool hasApiVersion)
    {
        if (hasApiVersion)
        {
            foreach (var kind in documentKinds)
            {
                sink.Add(kind);
            }
        }

        documentKinds.Clear();
    }

    private static IEnumerable<string> SplitLines(string text)
    {
        using var reader = new StringReader(text);
        while (reader.ReadLine() is { } line)
        {
            yield return line.TrimEnd();
        }
    }

    private static bool IsDocumentSeparator(string line)
    {
        var trimmed = line.Trim();
        return trimmed == "---" || trimmed.StartsWith("--- ", StringComparison.Ordinal);
    }

    private static bool IsComment(string line)
    {
        var trimmed = line.TrimStart();
        return trimmed.StartsWith('#');
    }

    private static string StripTemplateExpressions(string line)
    {
        var stripped = line.Contains("{{", StringComparison.Ordinal)
            ? TemplateExpression.Replace(line, string.Empty)
            : line;

        // An unbalanced opener (a multi-line template expression) swallows the rest of the line.
        var opener = stripped.IndexOf("{{", StringComparison.Ordinal);
        if (opener >= 0)
        {
            stripped = stripped[..opener];
        }

        return stripped.TrimEnd();
    }

    private static string? CleanKind(string raw)
    {
        var value = raw.Trim();

        var comment = value.IndexOf('#');
        if (comment >= 0)
        {
            value = value[..comment].Trim();
        }

        if (value.Length >= 2 &&
            ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
        {
            value = value[1..^1].Trim();
        }

        return value.Length > 0 && KindValue.IsMatch(value) ? value : null;
    }
}
