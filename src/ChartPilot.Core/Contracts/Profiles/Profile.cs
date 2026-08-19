using ChartPilot.Core.Checks;
using ChartPilot.Core.Values;

namespace ChartPilot.Core.Profiles;

/// <summary>
/// How sensitive the data a service handles is. Read from <c>platform.dataClassification</c> in values;
/// higher classifications promote security-relevant checks.
/// </summary>
public enum DataClassification
{
    Unclassified,
    Public,
    Internal,
    Confidential,
    SensitivePersonalData
}

/// <summary>Where a service is reachable from. Read from <c>platform.exposure</c> in values.</summary>
public enum Exposure
{
    Unknown,
    Internal,
    Public
}

/// <summary>
/// The mandatory requirements of a golden path profile. Each flag that is true promotes the
/// corresponding check from its default severity to Critical.
/// </summary>
public sealed record ProfileRequirements(
    bool RequireReadinessProbe = false,
    bool RequireLivenessProbe = false,
    bool RequireResourceRequests = false,
    bool RequireResourceLimits = false,
    bool RequireNetworkPolicy = false,
    bool RequirePodDisruptionBudget = false,
    bool RequireMtls = false,
    bool RequireAuthorizationPolicy = false,
    bool RequireDestinationRule = false,
    bool RequireServiceMonitor = false,
    bool RequireStandardLabels = false,
    bool RequireValuesSchema = false,
    bool RequirePinnedDependencies = false,
    bool RequireReadOnlyRootFilesystem = false,
    bool RequireNonRoot = false,
    bool DisallowLatestTag = false,
    bool DisallowInlineSecrets = false,
    bool AllowPublicIngress = true,
    int MinReplicas = 1);

/// <summary>Category weights for the overall score. They are expected to sum to 1.0.</summary>
public sealed record ScoreWeights(
    double Security = 0.35,
    double Reliability = 0.30,
    double Operability = 0.20,
    double Governance = 0.15);

/// <summary>Points deducted from a category score per finding of each severity.</summary>
public sealed record SeverityDeductions(int Critical = 25, int Warning = 8, int Info = 0);

/// <summary>
/// A golden path profile. A profile is data, not code: every profile runs the same catalog and
/// differs only in what it promotes, disables and weights.
/// </summary>
/// <param name="Id">A stable slug such as sensitive-member-data-service.</param>
/// <param name="SeverityOverrides">Check id to forced severity; wins over profile and classification promotion.</param>
/// <param name="DisabledChecks">Check ids that do not run at all under this profile.</param>
public sealed record Profile(
    string Id,
    string Name,
    string Description,
    ProfileRequirements Requirements,
    IReadOnlyDictionary<string, Severity> SeverityOverrides,
    IReadOnlyList<string> DisabledChecks,
    ScoreWeights Weights,
    SeverityDeductions Deductions);

/// <summary>The built-in profile catalog.</summary>
public interface IProfileStore
{
    IReadOnlyList<Profile> Profiles { get; }

    /// <summary>The profile used when the caller does not name one.</summary>
    Profile Default { get; }

    Profile? Get(string id);
}

/// <summary>
/// Resolves the actual severity of a finding: the check's default, promoted by the profile's
/// mandatory requirements, promoted again by the data classification, and finally overridden by
/// an explicit profile override.
/// </summary>
public interface ISeverityResolver
{
    Severity Resolve(CheckDescriptor descriptor, Profile profile, DataClassification classification);

    /// <summary>
    /// The resolved severity plus the sentence that explains it. "Why is this Critical for me and
    /// only a warning for my colleague?" is the question the promotion model most often provokes,
    /// and it is answerable from the same table that decided it.
    /// </summary>
    SeverityDecision Explain(CheckDescriptor descriptor, Profile profile, DataClassification classification);
}

/// <summary>A resolved severity and the reason it ended up there.</summary>
/// <param name="Severity">The severity the finding is reported at.</param>
/// <param name="Reason">
/// Why it differs from the rule's default, or null when the default was kept — a finding that was
/// never promoted needs no explanation beyond the rule itself.
/// </param>
public sealed record SeverityDecision(Severity Severity, string? Reason);

/// <summary>Reads the optional .chartpilot.yaml suppression file next to a chart.</summary>
public interface ISuppressionLoader
{
    /// <summary>Returns an empty list when the file is absent.</summary>
    IReadOnlyList<Suppression> Load(string chartDirectory);
}

/// <summary>Reads the platform metadata block a chart declares in its values.</summary>
public interface IPlatformMetadataReader
{
    /// <summary>Reads <c>platform.dataClassification</c>; Unclassified when absent or unrecognised.</summary>
    DataClassification ReadClassification(ValuesDocument values);

    /// <summary>Reads <c>platform.exposure</c>; Unknown when absent or unrecognised.</summary>
    Exposure ReadExposure(ValuesDocument values);
}
