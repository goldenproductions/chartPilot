using System.Text.RegularExpressions;

namespace ChartPilot.Core.Review;

/// <summary>
/// Pulls the offending template (and line, when Helm reported one) out of Helm's stderr, so the
/// GUI can point the editor at the failure instead of dumping a wall of text.
/// </summary>
public static partial class HelmErrorSource
{
    /// <summary>Returns something like <c>templates/deployment.yaml:12:14</c>, or null when stderr names no template.</summary>
    public static string? Extract(string? stdErr)
    {
        if (string.IsNullOrWhiteSpace(stdErr))
        {
            return null;
        }

        var match = TemplateReference().Match(stdErr);

        if (!match.Success)
        {
            return null;
        }

        var file = match.Groups["file"].Value;
        var line = match.Groups["line"].Value;
        var column = match.Groups["column"].Value;

        return column.Length > 0 ? $"{file}:{line}:{column}" : $"{file}:{line}";
    }

    [GeneratedRegex(
        @"(?<file>[A-Za-z0-9_\-./\\]+\.(?:yaml|yml|tpl)):(?<line>\d+)(?::(?<column>\d+))?",
        RegexOptions.CultureInvariant)]
    private static partial Regex TemplateReference();
}
