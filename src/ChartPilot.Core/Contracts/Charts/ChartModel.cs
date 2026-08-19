using System.Text.RegularExpressions;

namespace ChartPilot.Core.Charts;

/// <summary>A maintainer entry from Chart.yaml.</summary>
public sealed record ChartMaintainer(string Name, string? Email, string? Url);

/// <summary>A dependency entry from Chart.yaml (or its dependencies list).</summary>
public sealed record ChartDependency(
    string Name,
    string? Version,
    string? Repository,
    string? Condition,
    IReadOnlyList<string> Tags)
{
    private static readonly Regex ExactVersion = new(
        @"^\d+(\.\d+)*(-[0-9A-Za-z][0-9A-Za-z.-]*)?(\+[0-9A-Za-z][0-9A-Za-z.-]*)?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// True when <see cref="Version"/> is an exact version rather than a range.
    /// Range operators (<c>^ ~ * &gt; &lt; = |</c>), whitespace and wildcard segments (<c>1.2.x</c>) all
    /// make a dependency unpinned.
    /// </summary>
    public bool IsVersionPinned
    {
        get
        {
            var version = Version?.Trim();
            if (string.IsNullOrEmpty(version))
            {
                return false;
            }

            if (version.StartsWith('v') || version.StartsWith('V'))
            {
                version = version[1..];
            }

            foreach (var segment in version.Split('.'))
            {
                if (segment.Equals("x", StringComparison.OrdinalIgnoreCase) || segment == "*")
                {
                    return false;
                }
            }

            return ExactVersion.IsMatch(version);
        }
    }
}

/// <summary>A values file discovered next to Chart.yaml.</summary>
/// <param name="EnvironmentName">The <c>prod</c> in <c>values-prod.yaml</c>, or <c>null</c> for the default file.</param>
public sealed record ValuesFileInfo(string FileName, string FullPath, string? EnvironmentName, bool IsDefault);

/// <summary>A template file plus the kinds a cheap static scan believes it can emit.</summary>
public sealed record TemplateFileInfo(string RelativePath, IReadOnlyList<string> DetectedKinds);

/// <summary>
/// Everything ChartPilot knows about a chart before anything is rendered.
/// Produced by <see cref="IChartLoader"/> from the chart directory alone — no Helm process involved.
/// </summary>
public sealed record ChartModel(
    string ChartPath,
    string Name,
    string Version,
    string? AppVersion,
    string? Description,
    string? Type,
    string? KubeVersion,
    IReadOnlyList<ChartMaintainer> Maintainers,
    IReadOnlyList<ChartDependency> Dependencies,
    IReadOnlyList<ValuesFileInfo> ValuesFiles,
    bool HasValuesSchema,
    string? ValuesSchemaJson,
    IReadOnlyList<TemplateFileInfo> Templates,
    IReadOnlyList<string> DetectedKinds,
    bool HasSuppressionsFile);
