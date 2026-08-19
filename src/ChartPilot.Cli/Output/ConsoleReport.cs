using ChartPilot.Core.Checks;
using ChartPilot.Core.Checks.Guidance;
using ChartPilot.Core.Review;

namespace ChartPilot.Cli.Output;

/// <summary>The human-readable summary described in the spec's CLI sample.</summary>
internal static class ConsoleReport
{
    public static void Write(TextWriter output, ReviewResult result, bool reportWritten, bool explain = false)
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

        WriteGroup(output, result, Severity.Critical, "Critical", "[x]", explain);
        WriteGroup(output, result, Severity.Warning, "Warnings", "[!]", explain);
        WriteGroup(output, result, Severity.Info, "Info", "[i]", explain);

        if (result.Passed.Count > 0)
        {
            output.WriteLine();
            output.WriteLine($"Passed: {result.Passed.Count}");
        }

        if (result.Suppressed.Count > 0)
        {
            output.WriteLine($"Suppressed: {result.Suppressed.Count}");
        }

        if (!explain && result.Findings.Count > 0)
        {
            output.WriteLine();
            output.WriteLine("Run with --explain to see what each finding means and your options for fixing it.");
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
        string marker,
        bool explain)
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

            if (explain)
            {
                WriteExplanation(output, finding);
            }
        }
    }

    /// <summary>
    /// The --explain body for one finding: what it means, why it is this severity for this review,
    /// and the ways out. Indented under the finding so the list still scans without --explain.
    /// </summary>
    private static void WriteExplanation(TextWriter output, Finding finding)
    {
        var guidance = GuidanceCatalog.For(finding.CheckId);

        if (guidance is null)
        {
            return;
        }

        output.WriteLine();
        Wrap(output, "      ", guidance.WhatItMeans);

        if (!string.IsNullOrWhiteSpace(finding.SeverityReason))
        {
            output.WriteLine();
            Wrap(output, "      ", finding.SeverityReason!);
        }

        output.WriteLine();
        output.WriteLine("      Your options:");

        var number = 1;

        foreach (var option in guidance.Options)
        {
            var recommended = option.IsRecommended ? "  (recommended)" : string.Empty;
            output.WriteLine();
            output.WriteLine($"        {number}. {option.Title}{recommended}");
            Wrap(output, "           ", option.Summary);

            foreach (var line in option.Yaml.Split('\n'))
            {
                output.WriteLine($"             {line}");
            }

            Wrap(output, "           ", option.Tradeoff);
            number++;
        }

        output.WriteLine();
    }

    /// <summary>Wraps prose at 96 columns so an explanation stays readable in a terminal.</summary>
    private static void Wrap(TextWriter output, string indent, string text)
    {
        const int width = 96;
        var line = new System.Text.StringBuilder();

        foreach (var word in OneLine(text).Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length > 0 && indent.Length + line.Length + 1 + word.Length > width)
            {
                output.WriteLine(indent + line);
                line.Clear();
            }

            if (line.Length > 0)
            {
                line.Append(' ');
            }

            line.Append(word);
        }

        if (line.Length > 0)
        {
            output.WriteLine(indent + line);
        }
    }

    private static string OneLine(string? text)
        => string.IsNullOrWhiteSpace(text)
            ? string.Empty
            : string.Join(' ', text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}
