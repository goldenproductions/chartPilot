using ChartPilot.Core.Charts;
using ChartPilot.Core.Checks;
using ChartPilot.Core.Manifests;
using ChartPilot.Core.Profiles;
using ChartPilot.Core.Review;
using ChartPilot.Core.Scoring;
using YamlDotNet.RepresentationModel;

namespace ChartPilot.Core.Tests.Reporting;

/// <summary>
/// The review the report snapshots are taken from. It is deliberately hand-built rather than
/// produced by the pipeline: the report is a pure function of a ReviewResult, and the snapshot
/// should fail when the report changes, not when a rule changes.
/// </summary>
internal static class ReviewResultFactory
{
    public static ReviewResult SampleReview() => new(
        Chart: SampleChart(),
        Environment: "test",
        ProfileId: "sensitive-internal-service",
        Classification: DataClassification.SensitivePersonalData,
        Resources:
        [
            Resource("apps/v1", "Deployment", "member-api", "templates/deployment.yaml"),
            Resource("v1", "Service", "member-api", "templates/service.yaml"),
            Resource("networking.istio.io/v1", "VirtualService", "member-api", "templates/virtualservice.yaml"),
            Resource("cert-manager.io/v1", "Certificate", "member-api-tls", "templates/certificate.yaml")
        ],
        Findings:
        [
            new Finding(
                "CP-SEC-001",
                Severity.Critical,
                new ResourceRef("Deployment", "member-api"),
                "Container runs as root.",
                "Set securityContext.runAsNonRoot: true.",
                "spec.template.spec.containers[0]",
                "templates/deployment.yaml"),
            new Finding(
                "CP-NET-003",
                Severity.Critical,
                new ResourceRef("VirtualService", "member-api"),
                "Public route has no AuthorizationPolicy.",
                "Add an AuthorizationPolicy for the public route.",
                null,
                "templates/virtualservice.yaml"),
            new Finding(
                "CP-REL-004",
                Severity.Warning,
                new ResourceRef("Deployment", "member-api"),
                "No PodDisruptionBudget configured.",
                "Add a PodDisruptionBudget with minAvailable: 1."),
            new Finding(
                "CP-REL-002",
                Severity.Warning,
                new ResourceRef("Deployment", "member-api"),
                "livenessProbe missing.",
                "Add a livenessProbe to the container.",
                "spec.template.spec.containers[0].livenessProbe",
                "templates/deployment.yaml"),
            new Finding(
                "CP-GOV-001",
                Severity.Info,
                null,
                "Chart ships no values.schema.json.",
                "Add a values.schema.json.")
        ],
        Passed:
        [
            new PassedCheck("CP-REL-001", "Readiness probe configured", CheckCategory.Reliability,
                new ResourceRef("Deployment", "member-api")),
            new PassedCheck("CP-SEC-005", "Image tag is pinned", CheckCategory.Security,
                new ResourceRef("Deployment", "member-api"))
        ],
        Suppressed:
        [
            new SuppressedFinding(
                new Finding(
                    "CP-SEC-004",
                    Severity.Warning,
                    new ResourceRef("Deployment", "legacy-importer"),
                    "Root filesystem is writable.",
                    "Set readOnlyRootFilesystem: true."),
                "Vendor image requires a writable root filesystem; tracked in PLAT-412",
                new DateOnly(2026, 12, 1))
        ],
        Score: new ScoreReport(78,
        [
            new CategoryScore(CheckCategory.Security, 65, 2, 0, 0, 1),
            new CategoryScore(CheckCategory.Reliability, 80, 0, 2, 0, 1),
            new CategoryScore(CheckCategory.Operability, 85, 0, 0, 0, 0),
            new CategoryScore(CheckCategory.Governance, 70, 0, 0, 1, 0)
        ]),
        HelmVersion: "v4.2.4",
        GeneratedAt: new DateTimeOffset(2026, 8, 19, 10, 15, 0, TimeSpan.Zero));

    public static ChartModel SampleChart() => new(
        ChartPath: "/charts/member-api",
        Name: "member-api",
        Version: "0.3.1",
        AppVersion: "1.12.0",
        Description: "Member API",
        Type: "application",
        KubeVersion: null,
        Maintainers: [],
        Dependencies: [],
        ValuesFiles:
        [
            new ValuesFileInfo("values.yaml", "/charts/member-api/values.yaml", null, true),
            new ValuesFileInfo("values-dev.yaml", "/charts/member-api/values-dev.yaml", "dev", false),
            new ValuesFileInfo("values-prod.yaml", "/charts/member-api/values-prod.yaml", "prod", false)
        ],
        HasValuesSchema: false,
        ValuesSchemaJson: null,
        Templates: [],
        DetectedKinds: ["Deployment", "Service", "VirtualService", "Certificate"],
        HasSuppressionsFile: true);

    public static RenderedResource Resource(string apiVersion, string kind, string name, string sourceTemplate)
    {
        var yaml = $"apiVersion: {apiVersion}\nkind: {kind}\nmetadata:\n  name: {name}\n";
        var stream = new YamlStream();
        stream.Load(new StringReader(yaml));

        return new RenderedResource(apiVersion, kind, name, null, sourceTemplate, stream.Documents[0].RootNode, yaml);
    }
}
