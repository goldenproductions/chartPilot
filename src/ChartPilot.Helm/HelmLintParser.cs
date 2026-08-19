using System.Text.RegularExpressions;
using ChartPilot.Core.Helm;

namespace ChartPilot.Helm;

/// <summary>
/// Parses <c>helm lint</c> output. Helm emits one line per message in the shape
/// <c>[INFO] &lt;file&gt;: &lt;message&gt;</c>, surrounded by an <c>==&gt; Linting ./chart</c> header
/// and a <c>1 chart(s) linted, 0 chart(s) failed</c> summary. Only the bracketed lines are messages;
/// everything else is ignored.
/// </summary>
public static partial class HelmLintParser
{
    /// <summary>Parses lint output into messages, attributing file-less messages to <paramref name="fallbackFile"/>.</summary>
    public static IReadOnlyList<HelmLintMessage> Parse(string? output, string fallbackFile)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return [];
        }

        var messages = new List<HelmLintMessage>();

        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.Trim('\r', ' ', '\t');
            if (line.Length == 0)
            {
                continue;
            }

            var match = LinePattern().Match(line);
            if (!match.Success)
            {
                continue;
            }

            var severity = ParseSeverity(match.Groups["severity"].Value);
            var rest = match.Groups["rest"].Value.Trim();

            var (file, message) = SplitFile(rest, fallbackFile);
            messages.Add(new HelmLintMessage(severity, file, message));
        }

        return messages;
    }

    /// <summary>Maps a bracketed helm severity token onto <see cref="HelmLintSeverity"/>.</summary>
    public static HelmLintSeverity ParseSeverity(string token) => token.Trim().ToUpperInvariant() switch
    {
        "ERROR" => HelmLintSeverity.Error,
        "WARNING" => HelmLintSeverity.Warning,
        _ => HelmLintSeverity.Info
    };

    private static (string File, string Message) SplitFile(string rest, string fallbackFile)
    {
        if (rest.Length == 0)
        {
            return (fallbackFile, string.Empty);
        }

        var separator = rest.IndexOf(':');
        if (separator < 0)
        {
            return (fallbackFile, rest);
        }

        var candidate = rest[..separator].Trim();
        var remainder = rest[(separator + 1)..].Trim();

        // A file segment never contains whitespace; anything else is a message that happens to
        // contain a colon (for example "[ERROR] unable to parse: ...").
        if (candidate.Length == 0)
        {
            return (fallbackFile, remainder.Length == 0 ? rest.Trim(':', ' ') : remainder);
        }

        if (candidate.Any(char.IsWhiteSpace))
        {
            return (fallbackFile, rest);
        }

        return (candidate, remainder);
    }

    [GeneratedRegex(@"^\[(?<severity>INFO|WARNING|ERROR)\]\s*(?<rest>.*)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LinePattern();
}
