using System.Globalization;
using System.Text;
using ChartPilot.Core.Checks;
using ChartPilot.Core.Manifests;
using ChartPilot.Core.Profiles;
using ChartPilot.Core.Review;

namespace ChartPilot.Core.Reporting;

/// <summary>Renders a <see cref="ReviewResult"/> as text a human reviews.</summary>
public interface IReportWriter
{
    string Write(ReviewResult result);
}

/// <summary>
/// The Markdown review report: pasteable into a pull request as-is.
/// The output is deterministic — the only wall-clock value in the body is the explicit
/// <c>Generated at</c> line, which comes from <see cref="ReviewResult.GeneratedAt"/> — so it can be
/// snapshot tested and diffed between runs.
/// </summary>
public sealed class MarkdownReportWriter : IReportWriter
{
    private const string Nl = "\n";

    private static readonly IReadOnlyList<CheckCategory> CategoryOrder =
    [
        CheckCategory.Security,
        CheckCategory.Reliability,
        CheckCategory.Operability,
        CheckCategory.Governance
    ];

    private static readonly IReadOnlyList<ResourceCategory> ResourceCategoryOrder =
    [
        ResourceCategory.Workloads,
        ResourceCategory.Networking,
        ResourceCategory.Security,
        ResourceCategory.Certificates,
        ResourceCategory.Configuration,
        ResourceCategory.Scaling,
        ResourceCategory.Other
    ];

    public string Write(ReviewResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var sb = new StringBuilder();

        sb.Append("# ChartPilot Review: ").Append(result.Chart.Name).Append(Nl);

        WriteSummary(sb, result);
        WriteResources(sb, result);
        WriteFindings(sb, "Critical findings", result, Severity.Critical);
        WriteFindings(sb, "Warnings", result, Severity.Warning);
        WriteFindings(sb, "Info", result, Severity.Info);
        WritePassed(sb, result);
        WriteSuppressed(sb, result);
        WriteRecommendedActions(sb, result);

        return sb.ToString();
    }

    private static void WriteSummary(StringBuilder sb, ReviewResult result)
    {
        sb.Append(Nl).Append("## Summary").Append(Nl).Append(Nl);

        sb.Append("Overall score: **")
          .Append(result.Score.Overall.ToString(CultureInfo.InvariantCulture))
          .Append("/100**")
          .Append(Nl).Append(Nl);

        sb.Append("| Category | Score | Critical | Warning | Info | Passed |").Append(Nl);
        sb.Append("| --- | ---: | ---: | ---: | ---: | ---: |").Append(Nl);

        foreach (var category in CategoryOrder)
        {
            var score = result.Score[category];

            sb.Append("| ").Append(category)
              .Append(" | ").Append(Number(score?.Score))
              .Append(" | ").Append(Number(score?.CriticalCount))
              .Append(" | ").Append(Number(score?.WarningCount))
              .Append(" | ").Append(Number(score?.InfoCount))
              .Append(" | ").Append(Number(score?.PassedCount))
              .Append(" |").Append(Nl);
        }

        sb.Append(Nl);
        sb.Append("- Environment: `").Append(result.Environment).Append('`').Append(Nl);
        sb.Append("- Profile: `").Append(result.ProfileId).Append('`').Append(Nl);
        sb.Append("- Data classification: `").Append(Kebab(result.Classification)).Append('`').Append(Nl);
        sb.Append("- Chart version: `").Append(result.Chart.Version).Append('`').Append(Nl);
        sb.Append("- App version: `").Append(result.Chart.AppVersion ?? "not set").Append('`').Append(Nl);
        sb.Append("- Helm version: `").Append(result.HelmVersion ?? "unknown").Append('`').Append(Nl);
        sb.Append("- Generated at: ").Append(Timestamp(result.GeneratedAt)).Append(Nl);
    }

    private static void WriteResources(StringBuilder sb, ReviewResult result)
    {
        sb.Append(Nl).Append("## Rendered resources").Append(Nl);

        if (result.Resources.Count == 0)
        {
            sb.Append(Nl).Append("_Nothing was rendered._").Append(Nl);
            return;
        }

        var grouped = result.Resources
            .GroupBy(r => ResourceCategorizer.Categorize(r.Kind))
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var category in ResourceCategoryOrder)
        {
            if (!grouped.TryGetValue(category, out var resources))
            {
                continue;
            }

            sb.Append(Nl).Append("### ").Append(category).Append(Nl).Append(Nl);

            foreach (var resource in resources
                         .OrderBy(r => r.Kind, StringComparer.Ordinal)
                         .ThenBy(r => r.Name, StringComparer.Ordinal))
            {
                sb.Append("- ").Append(resource.Kind).Append('/').Append(resource.Name).Append(Nl);
            }
        }
    }

    private static void WriteFindings(StringBuilder sb, string heading, ReviewResult result, Severity severity)
    {
        sb.Append(Nl).Append("## ").Append(heading).Append(Nl).Append(Nl);

        var findings = Ordered(result.Findings.Where(f => f.Severity == severity)).ToList();

        if (findings.Count == 0)
        {
            sb.Append("_None._").Append(Nl);
            return;
        }

        foreach (var finding in findings)
        {
            sb.Append("- **").Append(finding.CheckId).Append("** ");

            if (finding.Resource is { } reference)
            {
                sb.Append(reference.Key).Append(" — ");
            }

            sb.Append(OneLine(finding.Message)).Append(Nl);

            var detail = Detail(finding);

            if (detail is not null)
            {
                sb.Append("  - ").Append(detail).Append(Nl);
            }

            if (!string.IsNullOrWhiteSpace(finding.Remediation))
            {
                WriteRemediation(sb, finding.Remediation, "  ");
            }
        }
    }

    private static string? Detail(Finding finding)
    {
        var hasTemplate = !string.IsNullOrWhiteSpace(finding.SourceTemplate);
        var hasPath = !string.IsNullOrWhiteSpace(finding.YamlPath);

        if (!hasTemplate && !hasPath)
        {
            return null;
        }

        if (hasTemplate && hasPath)
        {
            return $"*{finding.SourceTemplate}* `{finding.YamlPath}`";
        }

        return hasTemplate ? $"*{finding.SourceTemplate}*" : $"`{finding.YamlPath}`";
    }

    private static void WritePassed(StringBuilder sb, ReviewResult result)
    {
        sb.Append(Nl).Append("## Passed checks").Append(Nl).Append(Nl);

        if (result.Passed.Count == 0)
        {
            sb.Append("_None._").Append(Nl);
            return;
        }

        foreach (var passed in result.Passed
                     .OrderBy(p => p.CheckId, StringComparer.Ordinal)
                     .ThenBy(p => p.Resource?.Key ?? string.Empty, StringComparer.Ordinal))
        {
            sb.Append("- **").Append(passed.CheckId).Append("** ").Append(OneLine(passed.Title));

            if (passed.Resource is { } reference)
            {
                sb.Append(" — ").Append(reference.Key);
            }

            sb.Append(Nl);
        }
    }

    private static void WriteSuppressed(StringBuilder sb, ReviewResult result)
    {
        sb.Append(Nl).Append("## Suppressed").Append(Nl).Append(Nl);

        if (result.Suppressed.Count == 0)
        {
            sb.Append("_None._").Append(Nl);
            return;
        }

        sb.Append("| Check | Resource | Reason | Expires |").Append(Nl);
        sb.Append("| --- | --- | --- | --- |").Append(Nl);

        foreach (var suppressed in result.Suppressed
                     .OrderBy(s => s.Finding.CheckId, StringComparer.Ordinal)
                     .ThenBy(s => s.Finding.Resource?.Key ?? string.Empty, StringComparer.Ordinal))
        {
            sb.Append("| ").Append(suppressed.Finding.CheckId)
              .Append(" | ").Append(suppressed.Finding.Resource?.Key ?? "chart")
              .Append(" | ").Append(Cell(suppressed.Reason))
              .Append(" | ").Append(suppressed.Expires?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "never")
              .Append(" |").Append(Nl);
        }
    }

    private static void WriteRecommendedActions(StringBuilder sb, ReviewResult result)
    {
        sb.Append(Nl).Append("## Recommended actions").Append(Nl).Append(Nl);

        var actions = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var severity in new[] { Severity.Critical, Severity.Warning, Severity.Info })
        {
            foreach (var finding in Ordered(result.Findings.Where(f => f.Severity == severity)))
            {
                var action = (finding.Remediation ?? string.Empty).Trim();

                if (action.Length > 0 && seen.Add(OneLine(action)))
                {
                    actions.Add(action);
                }
            }
        }

        if (actions.Count == 0)
        {
            sb.Append("_No actions required._").Append(Nl);
            return;
        }

        for (var i = 0; i < actions.Count; i++)
        {
            var number = (i + 1).ToString(CultureInfo.InvariantCulture);

            if (IsMultiLine(actions[i]))
            {
                sb.Append(number).Append(". Apply:").Append(Nl);
                WriteFencedYaml(sb, actions[i], "   ");
            }
            else
            {
                sb.Append(number).Append(". ").Append(actions[i]).Append(Nl);
            }
        }
    }

    /// <summary>
    /// Remediations are frequently a YAML snippet the reader is meant to paste. Flattening those onto a
    /// single line produces text that is no longer valid YAML, so multi-line remediations are emitted as a
    /// fenced block instead.
    /// </summary>
    private static void WriteRemediation(StringBuilder sb, string remediation, string indent)
    {
        if (!IsMultiLine(remediation))
        {
            sb.Append(indent).Append("- Remediation: ").Append(OneLine(remediation)).Append(Nl);
            return;
        }

        sb.Append(indent).Append("- Remediation:").Append(Nl);
        WriteFencedYaml(sb, remediation, indent + "  ");
    }

    private static void WriteFencedYaml(StringBuilder sb, string yaml, string indent)
    {
        sb.Append(Nl).Append(indent).Append("```yaml").Append(Nl);

        foreach (var line in yaml.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            sb.Append(indent).Append(line.TrimEnd()).Append(Nl);
        }

        sb.Append(indent).Append("```").Append(Nl).Append(Nl);
    }

    private static bool IsMultiLine(string? text)
        => text is not null && text.AsSpan().IndexOfAny('\r', '\n') >= 0;

    private static IEnumerable<Finding> Ordered(IEnumerable<Finding> findings)
        => findings
            .OrderBy(f => f.CheckId, StringComparer.Ordinal)
            .ThenBy(f => f.Resource?.Key ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(f => f.YamlPath ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(f => f.Message, StringComparer.Ordinal);

    private static string Number(int? value)
        => value?.ToString(CultureInfo.InvariantCulture) ?? "-";

    private static string Timestamp(DateTimeOffset value)
        => value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static string OneLine(string? text)
        => string.IsNullOrWhiteSpace(text)
            ? string.Empty
            : string.Join(' ', text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private static string Cell(string? text)
        => OneLine(text).Replace("|", "\\|", StringComparison.Ordinal);

    /// <summary>SensitivePersonalData becomes sensitive-personal-data, matching the values file spelling.</summary>
    private static string Kebab(DataClassification classification)
    {
        var name = classification.ToString();
        var sb = new StringBuilder(name.Length + 4);

        for (var i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]))
            {
                sb.Append('-');
            }

            sb.Append(char.ToLowerInvariant(name[i]));
        }

        return sb.ToString();
    }
}
