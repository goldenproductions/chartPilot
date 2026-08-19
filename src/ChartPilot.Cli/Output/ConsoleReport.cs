using ChartPilot.Core.Checks;
using ChartPilot.Core.Review;

namespace ChartPilot.Cli.Output;

/// <summary>The human-readable summary described in the spec's CLI sample.</summary>
internal static class ConsoleReport
{
    public static void Write(TextWriter output, ReviewResult result, bool reportWritten)
    {
        output.WriteLine($"ChartPilot score: {result.Score.Overall}/100");
        output.WriteLine($"Critical: {result.CriticalCount}");
        output.WriteLine($"Warnings: {result.WarningCount}");

        if (result.InfoCount > 0)
        {
            output.WriteLine($"Info: {result.InfoCount}");
        }

        output.WriteLine();

        foreach (var category in result.Score.Categories)
        {
            output.WriteLine($"  {category.Category,-12} {category.Score,3}/100");
        }

        WriteGroup(output, result, Severity.Critical, "Critical", "[x]");
        WriteGroup(output, result, Severity.Warning, "Warnings", "[!]");
        WriteGroup(output, result, Severity.Info, "Info", "[i]");

        if (result.Passed.Count > 0)
        {
            output.WriteLine();
            output.WriteLine($"Passed: {result.Passed.Count}");
        }

        if (result.Suppressed.Count > 0)
        {
            output.WriteLine($"Suppressed: {result.Suppressed.Count}");
        }

        if (!reportWritten)
        {
            output.WriteLine();
            output.WriteLine("Run with --report report.md to export full review.");
        }
    }

    private static void WriteGroup(
        TextWriter output,
        ReviewResult result,
        Severity severity,
        string heading,
        string marker)
    {
        var findings = result.Findings
            .Where(f => f.Severity == severity)
            .OrderBy(f => f.CheckId, StringComparer.Ordinal)
            .ThenBy(f => f.Resource?.Key ?? string.Empty, StringComparer.Ordinal)
            .ToList();

        if (findings.Count == 0)
        {
            return;
        }

        output.WriteLine();
        output.WriteLine($"{heading}:");

        foreach (var finding in findings)
        {
            var resource = finding.Resource is null ? "chart" : finding.Resource.Key;
            output.WriteLine($"  {marker} {finding.CheckId} {resource}: {OneLine(finding.Message)}");
        }
    }

    private static string OneLine(string? text)
        => string.IsNullOrWhiteSpace(text)
            ? string.Empty
            : string.Join(' ', text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}
