namespace ChartPilot.Core.Checks;

/// <summary>
/// The static, profile-independent description of a check. Its <see cref="Id"/> is a stable
/// contract: suppressions, CI gating and report diffing are all keyed on it, so it never changes.
/// </summary>
/// <param name="Id">A rule id in the form CP-SEC-001 / CP-REL-004 / CP-NET-002 / CP-CERT-001 / CP-OBS-003 / CP-GOV-002.</param>
/// <param name="Remediation">Concrete guidance: the YAML to add, not a restatement of the problem.</param>
public sealed record CheckDescriptor(
    string Id,
    string Title,
    CheckCategory Category,
    Severity DefaultSeverity,
    string Rationale,
    string Remediation,
    string? DocsUrl = null);
