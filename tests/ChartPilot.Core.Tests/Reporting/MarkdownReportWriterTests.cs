using System.Text;
using ChartPilot.Core.Checks;
using ChartPilot.Core.Manifests;
using ChartPilot.Core.Reporting;
using ChartPilot.Core.Review;

namespace ChartPilot.Core.Tests.Reporting;

public sealed class MarkdownReportWriterTests
{
    private readonly MarkdownReportWriter _writer = new();

    [Fact]
    public void Write_matches_the_snapshot()
    {
        var actual = Normalize(_writer.Write(ReviewResultFactory.SampleReview()));

        // The report is user-facing text, so a change to it should be read as a diff rather than
        // rubber-stamped. Set CHARTPILOT_UPDATE_SNAPSHOTS=1 to rewrite the fixture, then review it.
        if (System.Environment.GetEnvironmentVariable("CHARTPILOT_UPDATE_SNAPSHOTS") == "1")
        {
            WriteSourceFixture("review-report.md", actual);
        }

        Assert.Equal(ReadFixture("review-report.md"), actual);
    }

    [Fact]
    public void Write_is_deterministic_across_calls()
    {
        var review = ReviewResultFactory.SampleReview();

        Assert.Equal(_writer.Write(review), _writer.Write(review));
    }

    [Fact]
    public void Write_puts_the_generated_timestamp_in_iso_8601_utc()
    {
        var review = ReviewResultFactory.SampleReview() with
        {
            GeneratedAt = new DateTimeOffset(2026, 8, 19, 14, 15, 0, TimeSpan.FromHours(2))
        };

        Assert.Contains("- Generated at: 2026-08-19T12:15:00Z", _writer.Write(review), StringComparison.Ordinal);
    }

    [Fact]
    public void Write_reports_empty_sections_rather_than_omitting_them()
    {
        var review = ReviewResultFactory.SampleReview() with
        {
            Findings = [],
            Passed = [],
            Suppressed = [],
            Resources = []
        };

        var markdown = _writer.Write(review);

        Assert.Contains("## Critical findings\n\n_None._", markdown, StringComparison.Ordinal);
        Assert.Contains("## Warnings\n\n_None._", markdown, StringComparison.Ordinal);
        Assert.Contains("## Passed checks\n\n_None._", markdown, StringComparison.Ordinal);
        Assert.Contains("## Suppressed\n\n_None._", markdown, StringComparison.Ordinal);
        Assert.Contains("_No actions required._", markdown, StringComparison.Ordinal);
        Assert.Contains("_Nothing was rendered._", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_lists_each_remediation_once_criticals_first()
    {
        var review = ReviewResultFactory.SampleReview() with
        {
            Findings =
            [
                new Finding("CP-REL-002", Severity.Warning, new ResourceRef("Deployment", "a"),
                    "Probe missing.", "Add a livenessProbe."),
                new Finding("CP-REL-002", Severity.Warning, new ResourceRef("Deployment", "b"),
                    "Probe missing.", "Add a livenessProbe."),
                new Finding("CP-SEC-001", Severity.Critical, new ResourceRef("Deployment", "a"),
                    "Runs as root.", "Set runAsNonRoot.")
            ]
        };

        // Scoped to the Recommended actions section: the guidance appendix that follows it also
        // numbers its options, and those are a different list with a different job.
        var actions = _writer.Write(review)
            .Split('\n')
            .SkipWhile(line => !line.StartsWith("## Recommended actions", StringComparison.Ordinal))
            .Skip(1)
            .TakeWhile(line => !line.StartsWith("## ", StringComparison.Ordinal))
            .Where(line => line.Length > 0 && char.IsDigit(line[0]))
            .ToList();

        Assert.Equal(new[] { "1. Set runAsNonRoot.", "2. Add a livenessProbe." }, actions);
    }

    [Fact]
    public void Write_escapes_pipes_in_a_suppression_reason()
    {
        var review = ReviewResultFactory.SampleReview() with
        {
            Suppressed =
            [
                new SuppressedFinding(
                    new Finding("CP-SEC-004", Severity.Warning, null, "Message.", "Fix it."),
                    "Waived | tracked in PLAT-1",
                    null)
            ]
        };

        var markdown = _writer.Write(review);

        Assert.Contains("| CP-SEC-004 | chart | Waived \\| tracked in PLAT-1 | never |", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_collapses_multi_line_messages_so_the_list_structure_survives()
    {
        var review = ReviewResultFactory.SampleReview() with
        {
            Findings =
            [
                new Finding("CP-SEC-001", Severity.Critical, new ResourceRef("Deployment", "a"),
                    "Runs as root.\nThe pod security context sets runAsUser: 0.", "Set runAsNonRoot.")
            ]
        };

        Assert.Contains(
            "- **CP-SEC-001** Deployment/a — Runs as root. The pod security context sets runAsUser: 0.",
            _writer.Write(review),
            StringComparison.Ordinal);
    }

    internal static string ReadFixture(string name)
        => Normalize(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "Reporting", name),
            Encoding.UTF8));


    /// <summary>
    /// Writes a fixture back to the source tree, not to the build output, so the regenerated file is
    /// the one that gets committed and reviewed.
    /// </summary>
    private static void WriteSourceFixture(string name, string content)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ChartPilot.Core.Tests.csproj")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            return;
        }

        File.WriteAllText(
            Path.Combine(directory.FullName, "Fixtures", "Reporting", name),
            content,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }
    internal static string Normalize(string text)
        => text.Replace("\r\n", "\n", StringComparison.Ordinal);
}
