using ChartPilot.Core.Checks;

namespace ChartPilot.Core.Profiles;

/// <summary>
/// The golden path profiles ChartPilot ships with.
/// <para>
/// A profile is data, not code (architecture.md 5.3): every profile runs the same catalog and
/// differs only in what it promotes to mandatory, what it turns off, and how it weights the score.
/// That is what makes adding an organization's own profile a YAML file rather than a release.
/// </para>
/// </summary>
public static class BuiltInProfiles
{
    private static readonly IReadOnlyDictionary<string, Severity> NoOverrides =
        new Dictionary<string, Severity>(StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlyList<string> NothingDisabled = Array.Empty<string>();

    /// <summary>The neutral profile: the whole catalog runs, nothing is mandatory, nothing is promoted.</summary>
    public static readonly Profile Default = new(
        "default",
        "Default",
        "Runs the whole catalog with no mandatory requirements. Findings keep their default severity, "
        + "which makes this the right profile for a first look at an unfamiliar chart.",
        new ProfileRequirements(),
        NoOverrides,
        NothingDisabled,
        new ScoreWeights(),
        new SeverityDeductions());

    /// <summary>Internet-facing services: availability and edge security are both mandatory.</summary>
    public static readonly Profile PublicWebService = new(
        "public-web-service",
        "Public web service",
        "An internet-facing service behind an ingress gateway. Availability requirements and edge "
        + "authorization are mandatory, and the image supply chain has to be pinned.",
        new ProfileRequirements(
            RequireReadinessProbe: true,
            RequireLivenessProbe: true,
            RequireResourceRequests: true,
            RequireResourceLimits: true,
            RequireNetworkPolicy: true,
            RequirePodDisruptionBudget: true,
            RequireMtls: true,
            RequireAuthorizationPolicy: true,
            RequireDestinationRule: true,
            RequireServiceMonitor: true,
            RequireStandardLabels: true,
            RequireValuesSchema: true,
            RequirePinnedDependencies: true,
            RequireReadOnlyRootFilesystem: true,
            RequireNonRoot: true,
            DisallowLatestTag: true,
            DisallowInlineSecrets: true,
            AllowPublicIngress: true,
            MinReplicas: 2),
        NoOverrides,
        NothingDisabled,
        new ScoreWeights(),
        new SeverityDeductions());

    /// <summary>Cluster-internal APIs: the same operational bar, without the public edge.</summary>
    public static readonly Profile InternalApi = new(
        "internal-api",
        "Internal API",
        "A service consumed only from inside the mesh. The reliability and observability bar is the "
        + "same as for a public service, but public ingress is not part of the golden path.",
        new ProfileRequirements(
            RequireReadinessProbe: true,
            RequireLivenessProbe: true,
            RequireResourceRequests: true,
            RequireResourceLimits: true,
            RequireNetworkPolicy: true,
            RequirePodDisruptionBudget: true,
            RequireMtls: true,
            RequireAuthorizationPolicy: false,
            RequireDestinationRule: true,
            RequireServiceMonitor: true,
            RequireStandardLabels: true,
            RequireValuesSchema: true,
            RequirePinnedDependencies: true,
            RequireReadOnlyRootFilesystem: false,
            RequireNonRoot: true,
            DisallowLatestTag: true,
            DisallowInlineSecrets: true,
            AllowPublicIngress: false,
            MinReplicas: 2),
        NoOverrides,
        NothingDisabled,
        new ScoreWeights(),
        new SeverityDeductions());

    /// <summary>The strictest profile: services handling member or personal data.</summary>
    public static readonly Profile SensitiveMemberDataService = new(
        "sensitive-member-data-service",
        "Sensitive member data service",
        "A service that processes member or personal data. Network isolation, strict mTLS, "
        + "authorization, resource limits and non-root execution are all mandatory, secrets may not be "
        + "inlined, and the service may not be exposed publicly.",
        new ProfileRequirements(
            RequireReadinessProbe: true,
            RequireLivenessProbe: true,
            RequireResourceRequests: true,
            RequireResourceLimits: true,
            RequireNetworkPolicy: true,
            RequirePodDisruptionBudget: true,
            RequireMtls: true,
            RequireAuthorizationPolicy: true,
            RequireDestinationRule: true,
            RequireServiceMonitor: true,
            RequireStandardLabels: true,
            RequireValuesSchema: true,
            RequirePinnedDependencies: true,
            RequireReadOnlyRootFilesystem: true,
            RequireNonRoot: true,
            DisallowLatestTag: true,
            DisallowInlineSecrets: true,
            AllowPublicIngress: false,
            MinReplicas: 2),
        NoOverrides,
        NothingDisabled,
        // Security carries more of the overall score than it does on the default path.
        new ScoreWeights(Security: 0.45, Reliability: 0.25, Operability: 0.15, Governance: 0.15),
        new SeverityDeductions());

    /// <summary>Scheduled work: probes and disruption budgets do not apply, but supply chain and limits do.</summary>
    public static readonly Profile BatchJob = new(
        "batch-job",
        "Batch job",
        "Scheduled or one-shot work. Serving-path rules (probes, PodDisruptionBudget, rolling update "
        + "strategy, autoscaling) are turned off, while resource limits and image pinning still apply.",
        new ProfileRequirements(
            RequireResourceRequests: true,
            RequireResourceLimits: true,
            RequireNetworkPolicy: false,
            RequireStandardLabels: true,
            RequireValuesSchema: false,
            RequirePinnedDependencies: true,
            RequireNonRoot: true,
            DisallowLatestTag: true,
            DisallowInlineSecrets: true,
            AllowPublicIngress: false,
            MinReplicas: 1),
        NoOverrides,
        [
            "CP-REL-001", // readinessProbe
            "CP-REL-002", // livenessProbe
            "CP-REL-005", // replica floor
            "CP-REL-006", // PodDisruptionBudget
            "CP-REL-007", // rolling update strategy
            "CP-REL-008", // startupProbe
            "CP-REL-010", // autoscaling
            "CP-OBS-003"  // named http/metrics port
        ],
        new ScoreWeights(Security: 0.35, Reliability: 0.20, Operability: 0.20, Governance: 0.25),
        new SeverityDeductions());

    /// <summary>Charts wrapping vendor images that cannot meet the modern baseline yet.</summary>
    public static readonly Profile LegacyIntegrationService = new(
        "legacy-integration-service",
        "Legacy integration service",
        "A chart around a vendor or legacy image that cannot yet meet the hardening baseline. The "
        + "container-hardening rules are demoted to informational so the genuinely dangerous findings "
        + "stay visible, but supply chain and secret handling are still enforced.",
        new ProfileRequirements(
            RequireReadinessProbe: true,
            RequireResourceRequests: true,
            RequireResourceLimits: true,
            RequireStandardLabels: false,
            RequirePinnedDependencies: true,
            DisallowLatestTag: true,
            DisallowInlineSecrets: true,
            AllowPublicIngress: false,
            MinReplicas: 1),
        new Dictionary<string, Severity>(StringComparer.OrdinalIgnoreCase)
        {
            ["CP-SEC-004"] = Severity.Info, // readOnlyRootFilesystem
            ["CP-SEC-011"] = Severity.Info, // allowPrivilegeEscalation
            ["CP-SEC-012"] = Severity.Info, // drop ALL capabilities
            ["CP-REL-007"] = Severity.Info, // rolling update strategy
            ["CP-OBS-002"] = Severity.Info  // standard labels
        },
        NothingDisabled,
        new ScoreWeights(),
        new SeverityDeductions());

    /// <summary>Throwaway environments: only the findings that can hurt the cluster itself are kept.</summary>
    public static readonly Profile SandboxService = new(
        "sandbox-service",
        "Sandbox service",
        "A throwaway or experimental workload. Nothing is mandatory and the operational rules are "
        + "turned off entirely; what remains is the small set of findings that can damage the cluster "
        + "or leak a credential regardless of how temporary the service is.",
        new ProfileRequirements(AllowPublicIngress: true, MinReplicas: 1),
        new Dictionary<string, Severity>(StringComparer.OrdinalIgnoreCase)
        {
            ["CP-SEC-005"] = Severity.Info, // a moving tag is the point of a sandbox
            ["CP-SEC-006"] = Severity.Info
        },
        [
            "CP-REL-005", "CP-REL-006", "CP-REL-007", "CP-REL-008", "CP-REL-010",
            "CP-SEC-004", "CP-SEC-008", "CP-SEC-009", "CP-SEC-011", "CP-SEC-012",
            "CP-NET-002", "CP-NET-003", "CP-NET-004", "CP-NET-005", "CP-NET-006",
            "CP-CERT-001", "CP-CERT-002", "CP-CERT-004",
            "CP-OBS-001", "CP-OBS-002", "CP-OBS-003", "CP-OBS-004",
            "CP-GOV-001", "CP-GOV-002", "CP-GOV-003", "CP-GOV-004", "CP-GOV-007", "CP-GOV-008"
        ],
        new ScoreWeights(),
        // Half deductions: a sandbox score should still move, without reading as a failing service.
        new SeverityDeductions(Critical: 12, Warning: 4, Info: 0));

    /// <summary>Every built-in profile, with <see cref="Default"/> first.</summary>
    public static readonly IReadOnlyList<Profile> All =
    [
        Default,
        PublicWebService,
        InternalApi,
        SensitiveMemberDataService,
        BatchJob,
        LegacyIntegrationService,
        SandboxService
    ];
}
