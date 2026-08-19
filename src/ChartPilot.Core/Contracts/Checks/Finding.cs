using ChartPilot.Core.Manifests;

namespace ChartPilot.Core.Checks;

/// <summary>A single violation reported by a check.</summary>
/// <param name="Severity">The resolved severity, not the check's default.</param>
/// <param name="Resource">The offending resource, or null for a chart-level finding.</param>
/// <param name="YamlPath">Where in the resource the problem is, i.e. spec.template.spec.containers[0].</param>
public sealed record Finding(
    string CheckId,
    Severity Severity,
    ResourceRef? Resource,
    string Message,
    string Remediation,
    string? YamlPath = null,
    string? SourceTemplate = null);

/// <summary>A check that ran and found nothing wrong. Passed checks are shown, and they count towards the score.</summary>
public sealed record PassedCheck(string CheckId, string Title, CheckCategory Category, ResourceRef? Resource = null);

/// <summary>
/// An entry from the chart's .chartpilot.yaml. A reason is mandatory, and an expiry turns a
/// permanent exception into a tracked one.
/// </summary>
/// <param name="Id">The check id being suppressed.</param>
/// <param name="Resource">A resource key such as Deployment/legacy-importer, or null for every resource.</param>
public sealed record Suppression(string Id, string? Resource, string Reason, DateOnly? Expires)
{
    /// <summary>True when this suppression has an expiry date that is already in the past.</summary>
    public bool IsExpired(DateOnly today) => Expires is { } expires && expires < today;
}

/// <summary>A finding that was raised and then suppressed. Kept so the report can show what was waived and why.</summary>
public sealed record SuppressedFinding(Finding Finding, string Reason, DateOnly? Expires);
