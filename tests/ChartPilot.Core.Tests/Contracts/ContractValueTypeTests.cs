using ChartPilot.Core.Charts;
using ChartPilot.Core.Checks;
using ChartPilot.Core.Manifests;
using ChartPilot.Core.Profiles;
using ChartPilot.Core.Review;
using ChartPilot.Core.Scoring;
using YamlDotNet.RepresentationModel;

namespace ChartPilot.Core.Tests.Contracts;

public class ContractValueTypeTests
{
    [Fact]
    public void ResourceRef_key_is_kind_slash_name()
    {
        var reference = new ResourceRef("Deployment", "member-api", "member-platform");

        Assert.Equal("Deployment/member-api", reference.Key);
        Assert.Equal("Deployment/member-api", reference.ToString());
    }

    [Fact]
    public void RenderedResource_exposes_its_ref_and_api_group()
    {
        var istio = new RenderedResource(
            "networking.istio.io/v1",
            "VirtualService",
            "member-api",
            "member-platform",
            "member-api/templates/virtualservice.yaml",
            new YamlMappingNode(),
            "kind: VirtualService");

        Assert.Equal("networking.istio.io", istio.ApiGroup);
        Assert.Equal(new ResourceRef("VirtualService", "member-api", "member-platform"), istio.Ref);
        Assert.Equal(istio.Ref, ResourceRef.From(istio));

        var core = istio with { ApiVersion = "v1", Kind = "Service" };
        Assert.Equal(string.Empty, core.ApiGroup);
    }

    [Theory]
    [InlineData("Deployment", ResourceCategory.Workloads)]
    [InlineData("CronJob", ResourceCategory.Workloads)]
    [InlineData("VirtualService", ResourceCategory.Networking)]
    [InlineData("Service", ResourceCategory.Networking)]
    [InlineData("NetworkPolicy", ResourceCategory.Security)]
    [InlineData("Secret", ResourceCategory.Security)]
    [InlineData("Certificate", ResourceCategory.Certificates)]
    [InlineData("ClusterIssuer", ResourceCategory.Certificates)]
    [InlineData("ConfigMap", ResourceCategory.Configuration)]
    [InlineData("PodDisruptionBudget", ResourceCategory.Scaling)]
    [InlineData("HorizontalPodAutoscaler", ResourceCategory.Scaling)]
    [InlineData("SomeVendorThing", ResourceCategory.Other)]
    [InlineData("deployment", ResourceCategory.Other)]
    public void Categorize_maps_kinds_onto_explorer_groups(string kind, ResourceCategory expected)
    {
        Assert.Equal(expected, ResourceCategorizer.Categorize(kind));
    }

    [Theory]
    [InlineData("1.4.2", true)]
    [InlineData("v1.4.2", true)]
    [InlineData("18.19.4", true)]
    [InlineData("1.4.2-rc.1", true)]
    [InlineData("^1.4.2", false)]
    [InlineData("~1.4", false)]
    [InlineData("1.2.x", false)]
    [InlineData("*", false)]
    [InlineData(">=1.0.0 <2.0.0", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Dependency_pinning_rejects_ranges_and_wildcards(string? version, bool expected)
    {
        var dependency = new ChartDependency("redis", version, "https://charts.example.com", null, []);

        Assert.Equal(expected, dependency.IsVersionPinned);
    }

    [Fact]
    public void Suppression_expiry_is_compared_against_today()
    {
        var today = new DateOnly(2026, 8, 19);

        Assert.True(new Suppression("CP-SEC-004", null, "tracked in PLAT-412", new DateOnly(2026, 8, 18)).IsExpired(today));
        Assert.False(new Suppression("CP-SEC-004", null, "tracked in PLAT-412", new DateOnly(2026, 8, 19)).IsExpired(today));
        Assert.False(new Suppression("CP-SEC-004", null, "tracked in PLAT-412", null).IsExpired(today));
    }

    [Fact]
    public void Severity_orders_info_below_warning_below_critical()
    {
        Assert.True(Severity.Critical > Severity.Warning);
        Assert.True(Severity.Warning > Severity.Info);
    }

    [Fact]
    public void ScoreReport_indexer_finds_a_category_or_returns_null()
    {
        var report = new ScoreReport(78,
        [
            new CategoryScore(CheckCategory.Security, 65, 1, 2, 0, 4),
            new CategoryScore(CheckCategory.Reliability, 80, 0, 2, 1, 6)
        ]);

        Assert.Equal(65, report[CheckCategory.Security]!.Score);
        Assert.Equal(80, report[CheckCategory.Reliability]!.Score);
        Assert.Null(report[CheckCategory.Governance]);
    }

    [Fact]
    public void ReviewResult_counts_findings_by_severity()
    {
        var chart = new ChartModel(
            ChartPath: @"C:\charts\member-api",
            Name: "member-api",
            Version: "0.3.1",
            AppVersion: "1.12.0",
            Description: null,
            Type: "application",
            KubeVersion: null,
            Maintainers: [],
            Dependencies: [],
            ValuesFiles: [],
            HasValuesSchema: false,
            ValuesSchemaJson: null,
            Templates: [],
            DetectedKinds: [],
            HasSuppressionsFile: false);

        var result = new ReviewResult(
            chart,
            "prod",
            "sensitive-member-data-service",
            DataClassification.SensitivePersonalData,
            [],
            [
                new Finding("CP-SEC-001", Severity.Critical, null, "runs as root", "set runAsNonRoot: true"),
                new Finding("CP-NET-002", Severity.Critical, null, "no AuthorizationPolicy", "add one"),
                new Finding("CP-REL-002", Severity.Warning, null, "no livenessProbe", "add one"),
                new Finding("CP-OBS-002", Severity.Info, null, "no standard labels", "add them")
            ],
            [],
            [],
            new ScoreReport(78, []),
            "v4.2.4",
            DateTimeOffset.UnixEpoch);

        Assert.Equal(2, result.CriticalCount);
        Assert.Equal(1, result.WarningCount);
        Assert.Equal(1, result.InfoCount);
    }
}
