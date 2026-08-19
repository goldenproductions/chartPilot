using System.Collections.Frozen;

namespace ChartPilot.Core.Checks.Guidance;

/// <summary>
/// The authored guidance for every rule, keyed by check id.
///
/// <para>
/// Guidance is deliberately separate from the rule that produces the finding: rules are logic and
/// change when Kubernetes does, guidance is prose and changes when our advice does. Keeping them
/// apart means the prose can be reviewed as prose, all of it in one place.
/// </para>
/// <para>
/// A rule without an entry here is a build-time gap, not a runtime one —
/// <c>GuidanceCatalogTests</c> fails when a registered check has no guidance, which is what stops
/// the catalog rotting as rules are added.
/// </para>
/// </summary>
public static class GuidanceCatalog
{
    private static readonly FrozenDictionary<string, CheckGuidance> Entries =
        SecurityGuidance.Entries()
            .Concat(ReliabilityGuidance.Entries())
            .Concat(NetworkGuidance.Entries())
            .Concat(CertificateGuidance.Entries())
            .Concat(ObservabilityGuidance.Entries())
            .Concat(GovernanceGuidance.Entries())
            .ToFrozenDictionary(e => e.Key, e => e.Value, StringComparer.OrdinalIgnoreCase);

    /// <summary>Every check id that has authored guidance.</summary>
    public static IReadOnlyCollection<string> CoveredCheckIds => Entries.Keys;

    /// <summary>The guidance for a check, or null when none is authored.</summary>
    public static CheckGuidance? For(string checkId)
        => checkId is not null && Entries.TryGetValue(checkId, out var guidance) ? guidance : null;
}
