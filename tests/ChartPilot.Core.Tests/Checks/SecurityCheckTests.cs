using ChartPilot.Core.Checks;
using ChartPilot.Core.Profiles;

namespace ChartPilot.Core.Tests.Checks;

public class SecurityCheckTests
{
    private const string Bad = "security/workload-bad.yaml";
    private const string Good = "security/workload-good.yaml";

    [Fact]
    public void RunAsRoot_IsCritical()
    {
        var finding = Assert.Single(CheckTestHarness.FindingsFor("CP-SEC-001", Bad));

        Assert.Equal(Severity.Critical, finding.Severity);
        Assert.Equal("Deployment/member-api", finding.Resource?.Key);
        Assert.Equal("spec.template.spec.containers[0].securityContext.runAsUser", finding.YamlPath);
        Assert.True(CheckTestHarness.Passed("CP-SEC-001", Good));
    }

    [Fact]
    public void PrivilegedContainer_IsCritical()
    {
        var finding = Assert.Single(CheckTestHarness.FindingsFor("CP-SEC-002", Bad));

        Assert.Equal(Severity.Critical, finding.Severity);
        Assert.Equal("spec.template.spec.containers[0].securityContext.privileged", finding.YamlPath);
        Assert.True(CheckTestHarness.Passed("CP-SEC-002", Good));
    }

    [Fact]
    public void MissingRunAsNonRoot_IsWarning_AndInheritsThePodSecurityContext()
    {
        var finding = Assert.Single(CheckTestHarness.FindingsFor("CP-SEC-003", Bad));

        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Equal("spec.template.spec.containers[0].securityContext", finding.YamlPath);

        // The compliant fixture declares runAsNonRoot on the pod, not on the container.
        Assert.True(CheckTestHarness.Passed("CP-SEC-003", Good));
    }

    [Fact]
    public void WritableRootFilesystem_IsReported()
    {
        var finding = Assert.Single(CheckTestHarness.FindingsFor("CP-SEC-004", Bad));

        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.True(CheckTestHarness.Passed("CP-SEC-004", Good));
    }

    [Fact]
    public void LatestTag_IsCritical()
    {
        var finding = Assert.Single(CheckTestHarness.FindingsFor("CP-SEC-005", Bad));

        Assert.Equal(Severity.Critical, finding.Severity);
        Assert.Equal("spec.template.spec.containers[0].image", finding.YamlPath);
        Assert.Contains("latest", finding.Message, StringComparison.Ordinal);
        Assert.True(CheckTestHarness.Passed("CP-SEC-005", Good));
    }

    [Fact]
    public void MovingTag_IsReportedByThePinningRule_ButLatestIsNot()
    {
        var finding = Assert.Single(CheckTestHarness.FindingsFor("CP-SEC-006", "security/image-unpinned.yaml"));

        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Contains("stable", finding.Message, StringComparison.Ordinal);

        // A digest is pinned, and a `latest` tag belongs to CP-SEC-005 rather than to this rule.
        Assert.True(CheckTestHarness.Passed("CP-SEC-006", "security/image-pinned.yaml"));
        Assert.True(CheckTestHarness.Passed("CP-SEC-006", Bad));
    }

    [Fact]
    public void InlineSecret_IsReported_ForBothARenderedSecretAndALiteralEnvVar()
    {
        var fromSecret = Assert.Single(CheckTestHarness.FindingsFor("CP-SEC-007", "security/secret-bad.yaml"));
        Assert.Equal(Severity.Critical, fromSecret.Severity);
        Assert.Equal("Secret/member-api-db", fromSecret.Resource?.Key);
        Assert.Equal("stringData", fromSecret.YamlPath);

        var fromEnv = Assert.Single(CheckTestHarness.FindingsFor("CP-SEC-007", Bad));
        Assert.Equal("spec.template.spec.containers[0].env[0]", fromEnv.YamlPath);
        Assert.Contains("DB_PASSWORD", fromEnv.Message, StringComparison.Ordinal);

        Assert.True(CheckTestHarness.Passed("CP-SEC-007", "security/secret-good.yaml"));
        Assert.True(CheckTestHarness.Passed("CP-SEC-007", Good));
    }

    [Fact]
    public void MissingNetworkPolicy_IsReported()
    {
        var finding = Assert.Single(CheckTestHarness.FindingsFor("CP-SEC-008", Bad));

        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Equal("Deployment/member-api", finding.Resource?.Key);
        Assert.True(CheckTestHarness.Passed("CP-SEC-008", Good));
    }

    [Fact]
    public void ServiceAccountTokenAutomount_IsReported_WhenNotDisabled()
    {
        var explicitlyOn = Assert.Single(CheckTestHarness.FindingsFor("CP-SEC-009", "security/automount-enabled.yaml"));
        Assert.Contains("automountServiceAccountToken: true", explicitlyOn.Message, StringComparison.Ordinal);
        Assert.Equal("spec.template.spec.automountServiceAccountToken", explicitlyOn.YamlPath);

        var implicitlyOn = Assert.Single(CheckTestHarness.FindingsFor("CP-SEC-009", Bad));
        Assert.Equal(Severity.Warning, implicitlyOn.Severity);

        Assert.True(CheckTestHarness.Passed("CP-SEC-009", Good));
    }

    [Fact]
    public void WildcardRbac_IsReportedOncePerWildcardField()
    {
        var findings = CheckTestHarness.FindingsFor("CP-SEC-010", "security/rbac-bad.yaml");

        Assert.Equal(3, findings.Count);
        Assert.All(findings, f =>
        {
            Assert.Equal(Severity.Critical, f.Severity);
            Assert.Equal("ClusterRole/member-api", f.Resource?.Key);
        });
        Assert.Contains(findings, f => f.YamlPath == "rules[0].verbs");
        Assert.Contains(findings, f => f.YamlPath == "rules[0].resources");
        Assert.Contains(findings, f => f.YamlPath == "rules[0].apiGroups");

        Assert.True(CheckTestHarness.Passed("CP-SEC-010", "security/rbac-good.yaml"));
    }

    [Fact]
    public void RbacCheck_DoesNotRun_WhenTheChartShipsNoRoles()
    {
        var result = CheckTestHarness.Run(CheckTestHarness.Context(TestGraph.FromFixture(Good)));

        Assert.DoesNotContain(result.Findings, f => f.CheckId == "CP-SEC-010");
        Assert.DoesNotContain(result.Passed, p => p.CheckId == "CP-SEC-010");
    }

    [Fact]
    public void PrivilegeEscalation_IsReported()
    {
        var finding = Assert.Single(CheckTestHarness.FindingsFor("CP-SEC-011", Bad));

        Assert.Equal("spec.template.spec.containers[0].securityContext", finding.YamlPath);
        Assert.True(CheckTestHarness.Passed("CP-SEC-011", Good));
    }

    [Fact]
    public void UndroppedCapabilities_AreReported()
    {
        var finding = Assert.Single(CheckTestHarness.FindingsFor("CP-SEC-012", Bad));

        Assert.Equal("spec.template.spec.containers[0].securityContext.capabilities", finding.YamlPath);
        Assert.True(CheckTestHarness.Passed("CP-SEC-012", Good));
    }

    [Fact]
    public void HostNamespace_IsCritical()
    {
        var finding = Assert.Single(CheckTestHarness.FindingsFor("CP-SEC-013", Bad));

        Assert.Equal(Severity.Critical, finding.Severity);
        Assert.Equal("spec.template.spec.hostNetwork", finding.YamlPath);
        Assert.True(CheckTestHarness.Passed("CP-SEC-013", Good));
    }

    [Fact]
    public void SensitiveDataClassification_PromotesTheNetworkPolicyRuleToCritical()
    {
        var findings = CheckTestHarness.FindingsFor(
            "CP-SEC-008",
            Bad,
            BuiltInProfiles.Default,
            DataClassification.SensitivePersonalData);

        Assert.Equal(Severity.Critical, Assert.Single(findings).Severity);
    }

    // ------------------------------------------------------------------ CP-SEC-014

    private static CheckRunResult RunExposure(
        string fixture,
        Profile? profile = null,
        Exposure exposure = Exposure.Unknown,
        DataClassification classification = DataClassification.Unclassified)
        => CheckTestHarness.Run(CheckTestHarness.Context(
            TestGraph.FromFixture(fixture),
            profile,
            classification,
            exposure: exposure));

    [Fact]
    public void PublicExposureRule_DoesNotRun_WhenPublicIngressIsAllowed()
    {
        var result = RunExposure("security/public-exposure.yaml");

        Assert.DoesNotContain(result.Findings, f => f.CheckId == "CP-SEC-014");
        Assert.DoesNotContain(result.Passed, p => p.CheckId == "CP-SEC-014");
    }

    [Fact]
    public void PublicExposure_IsReported_WhenTheProfileForbidsPublicIngress()
    {
        var profile = BuiltInProfiles.Default with
        {
            Id = "internal-only",
            Requirements = BuiltInProfiles.Default.Requirements with { AllowPublicIngress = false }
        };

        var findings = RunExposure("security/public-exposure.yaml", profile)
            .Findings
            .Where(f => f.CheckId == "CP-SEC-014")
            .ToArray();

        // The Ingress, the LoadBalancer Service and the bound public Gateway are three separate routes.
        Assert.Equal(3, findings.Length);
        Assert.All(findings, f => Assert.Equal(Severity.Critical, f.Severity));
        Assert.Contains(findings, f => f.Resource?.Key == "Ingress/member-api");
        Assert.Contains(findings, f => f.Resource?.Key == "Service/member-api-lb");
        Assert.Contains(findings, f => f.Resource?.Key == "Gateway/public-gateway");
        Assert.All(findings, f => Assert.Contains("allowPublicIngress: false", f.Message, StringComparison.Ordinal));
    }

    [Fact]
    public void PublicExposure_IsReported_WhenTheChartDeclaresItselfInternal()
    {
        var finding = Assert.Single(
            RunExposure("security/public-exposure.yaml", exposure: Exposure.Internal).Findings,
            f => f.CheckId == "CP-SEC-014" && f.Resource?.Key == "Ingress/member-api");

        Assert.Contains("platform.exposure: internal", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PublicExposure_IsCritical_ForSensitivePersonalData()
    {
        var findings = RunExposure(
                "security/public-exposure.yaml",
                classification: DataClassification.SensitivePersonalData)
            .Findings
            .Where(f => f.CheckId == "CP-SEC-014")
            .ToArray();

        Assert.NotEmpty(findings);
        Assert.All(findings, f => Assert.Equal(Severity.Critical, f.Severity));
    }

    [Fact]
    public void InternalRoutes_PassThePublicExposureRule()
    {
        var result = RunExposure("security/internal-exposure.yaml", exposure: Exposure.Internal);

        Assert.DoesNotContain(result.Findings, f => f.CheckId == "CP-SEC-014");
        Assert.Contains(result.Passed, p => p.CheckId == "CP-SEC-014");
    }
}
