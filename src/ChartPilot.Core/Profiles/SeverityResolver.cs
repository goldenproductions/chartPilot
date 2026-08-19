using ChartPilot.Core.Checks;

namespace ChartPilot.Core.Profiles;

/// <summary>
/// Resolves the severity of a finding in exactly the order architecture.md 5.3 lays out:
/// <list type="number">
/// <item>start at the check's <see cref="CheckDescriptor.DefaultSeverity"/>;</item>
/// <item>promote to Critical when the active profile marks the matching requirement mandatory;</item>
/// <item>promote again by data classification;</item>
/// <item>apply the profile's explicit <see cref="Profile.SeverityOverrides"/>, which win outright.</item>
/// </list>
/// <para>
/// Promotion never demotes: steps 2 and 3 can only raise the severity. An override is the single
/// mechanism that can lower one, which keeps "why is this Critical?" answerable from one table.
/// </para>
/// </summary>
public sealed class SeverityResolver : ISeverityResolver
{
    /// <summary>
    /// Which check ids each profile requirement governs. This is the whole profile-to-catalog
    /// mapping — one readable table rather than a promotion rule scattered across the rules.
    /// </summary>
    private static readonly (Func<ProfileRequirements, bool> IsMandatory, string[] CheckIds)[] RequirementMap =
    [
        (r => r.RequireReadinessProbe,          ["CP-REL-001"]),
        (r => r.RequireLivenessProbe,           ["CP-REL-002"]),
        (r => r.RequireResourceRequests,        ["CP-REL-003"]),
        (r => r.RequireResourceLimits,          ["CP-REL-004"]),
        (r => r.MinReplicas > 1,                ["CP-REL-005"]),
        (r => r.RequirePodDisruptionBudget,     ["CP-REL-006"]),
        (r => r.RequireNetworkPolicy,           ["CP-SEC-008"]),
        (r => r.RequireNonRoot,                 ["CP-SEC-001", "CP-SEC-003"]),
        (r => r.RequireReadOnlyRootFilesystem,  ["CP-SEC-004"]),
        (r => r.DisallowLatestTag,              ["CP-SEC-005", "CP-SEC-006"]),
        (r => r.DisallowInlineSecrets,          ["CP-SEC-007"]),
        (r => r.RequireMtls,                    ["CP-NET-003"]),
        (r => r.RequireAuthorizationPolicy,     ["CP-NET-002", "CP-NET-008"]),
        (r => r.RequireDestinationRule,         ["CP-NET-004"]),
        // A profile that forbids public ingress cares a great deal about an unguarded public route,
        // and CP-SEC-014 is the rule that reports the public route itself.
        (r => !r.AllowPublicIngress,            ["CP-NET-001", "CP-NET-002", "CP-SEC-014"]),
        (r => r.RequireServiceMonitor,          ["CP-OBS-001"]),
        (r => r.RequireStandardLabels,          ["CP-OBS-002"]),
        (r => r.RequireValuesSchema,            ["CP-GOV-001"]),
        (r => r.RequirePinnedDependencies,      ["CP-GOV-002"])
    ];

    /// <summary>
    /// Checks that sensitive personal data forces to Critical regardless of the profile: the mesh
    /// encryption, the network boundary, the authorization decision, secret handling and the blast
    /// radius of a runaway container.
    /// </summary>
    private static readonly HashSet<string> SensitiveDataCritical = new(StringComparer.OrdinalIgnoreCase)
    {
        "CP-NET-003", // strict mTLS
        "CP-SEC-008", // NetworkPolicy
        "CP-NET-002", // AuthorizationPolicy on a public route
        "CP-SEC-007", // inline secrets
        "CP-REL-004", // resource limits
        "CP-SEC-014"  // a route from the public internet
    };

    public Severity Resolve(CheckDescriptor descriptor, Profile profile, DataClassification classification)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(profile);

        var severity = descriptor.DefaultSeverity;

        // 1. Profile requirements promote a check to mandatory.
        if (IsMandatedBy(profile.Requirements, descriptor.Id))
        {
            severity = Severity.Critical;
        }

        // 2. Data classification promotes on top of that.
        severity = PromoteForClassification(severity, descriptor.Id, classification);

        // 3. An explicit override wins outright, in either direction.
        if (profile.SeverityOverrides is { } overrides && overrides.Count > 0)
        {
            foreach (var entry in overrides)
            {
                if (string.Equals(entry.Key, descriptor.Id, StringComparison.OrdinalIgnoreCase))
                {
                    return entry.Value;
                }
            }
        }

        return severity;
    }

    /// <summary>True when the profile makes this check's subject a mandatory requirement.</summary>
    public static bool IsMandatedBy(ProfileRequirements requirements, string checkId)
    {
        foreach (var (isMandatory, checkIds) in RequirementMap)
        {
            if (!isMandatory(requirements))
            {
                continue;
            }

            foreach (var id in checkIds)
            {
                if (string.Equals(id, checkId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static Severity PromoteForClassification(Severity severity, string checkId, DataClassification classification)
        => classification switch
        {
            DataClassification.SensitivePersonalData when SensitiveDataCritical.Contains(checkId) => Severity.Critical,

            // Confidential data does not force anything to Critical, but it does mean nothing about
            // this service is merely informational. Sensitive data inherits the same floor.
            (DataClassification.Confidential or DataClassification.SensitivePersonalData)
                when severity == Severity.Info => Severity.Warning,

            _ => severity
        };
}
