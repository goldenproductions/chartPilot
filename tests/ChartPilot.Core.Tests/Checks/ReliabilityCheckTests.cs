using ChartPilot.Core.Checks;
using ChartPilot.Core.Profiles;

namespace ChartPilot.Core.Tests.Checks;

public class ReliabilityCheckTests
{
    private const string Bad = "reliability/deployment-bad.yaml";
    private const string Good = "reliability/deployment-good.yaml";

    [Fact]
    public void MissingReadinessProbe_IsReported_WithContainerPath()
    {
        var findings = CheckTestHarness.FindingsFor("CP-REL-001", Bad);

        var finding = Assert.Single(findings);
        Assert.Equal("CP-REL-001", finding.CheckId);
        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Equal("Deployment/member-api", finding.Resource?.Key);
        Assert.Equal("spec.template.spec.containers[0]", finding.YamlPath);
        Assert.Contains("readinessProbe", finding.Remediation, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadinessProbe_Present_IsReportedAsPassed()
    {
        Assert.Empty(CheckTestHarness.FindingsFor("CP-REL-001", Good));
        Assert.True(CheckTestHarness.Passed("CP-REL-001", Good));
    }

    [Fact]
    public void MissingLivenessProbe_IsReported()
    {
        var finding = Assert.Single(CheckTestHarness.FindingsFor("CP-REL-002", Bad));

        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Equal("spec.template.spec.containers[0]", finding.YamlPath);
        Assert.True(CheckTestHarness.Passed("CP-REL-002", Good));
    }

    [Fact]
    public void MissingResourceRequests_IsReported()
    {
        var finding = Assert.Single(CheckTestHarness.FindingsFor("CP-REL-003", Bad));

        Assert.Equal("spec.template.spec.containers[0].resources", finding.YamlPath);
        Assert.True(CheckTestHarness.Passed("CP-REL-003", Good));
    }

    [Fact]
    public void MissingResourceLimits_IsReported()
    {
        var finding = Assert.Single(CheckTestHarness.FindingsFor("CP-REL-004", Bad));

        Assert.Equal("spec.template.spec.containers[0].resources", finding.YamlPath);
        Assert.True(CheckTestHarness.Passed("CP-REL-004", Good));
    }

    /// <summary>
    /// The descriptor is titled "Container has no resource limits" and the profile flag is
    /// RequireResourceLimits: a memory limit on its own does not satisfy either.
    /// </summary>
    [Fact]
    public void ResourceLimits_CoverCpuAsWellAsMemory()
    {
        var finding = Assert.Single(
            CheckTestHarness.FindingsFor("CP-REL-004", "reliability/memory-limit-only.yaml"));

        Assert.Contains("resources.limits.cpu", finding.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("resources.limits.memory", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReplicaFloor_IsNotEvaluated_WhenTheProfileDoesNotDemandOne()
    {
        var context = CheckTestHarness.Context(TestGraph.FromFixture(Bad));
        var result = CheckTestHarness.Run(context);

        Assert.DoesNotContain(result.Findings, f => f.CheckId == "CP-REL-005");
        Assert.DoesNotContain(result.Passed, p => p.CheckId == "CP-REL-005");
    }

    [Fact]
    public void ReplicaFloor_IsCritical_UnderAProfileThatRequiresTwo()
    {
        var findings = CheckTestHarness.FindingsFor("CP-REL-005", Bad, BuiltInProfiles.SensitiveMemberDataService);

        var finding = Assert.Single(findings);
        Assert.Equal(Severity.Critical, finding.Severity);
        Assert.Equal("spec.replicas", finding.YamlPath);
        Assert.Contains("requires at least 2", finding.Message, StringComparison.Ordinal);

        Assert.True(CheckTestHarness.Passed("CP-REL-005", Good, BuiltInProfiles.SensitiveMemberDataService));
    }

    /// <summary>
    /// CheckContext.Environment is an input, not decoration: reviewing values-prod.yaml means the
    /// production floor applies even under a profile whose MinReplicas is 1. This is also what makes
    /// the rule's declared Warning severity reachable — a profile-mandated floor is always promoted
    /// to Critical by the severity resolver.
    /// </summary>
    [Fact]
    public void ReplicaFloor_AppliesInProduction_EvenUnderAPermissiveProfile()
    {
        var context = CheckTestHarness.Context(TestGraph.FromFixture(Bad), environment: "prod");

        var finding = Assert.Single(CheckTestHarness.Run(context).Findings, f => f.CheckId == "CP-REL-005");

        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Contains("environment 'prod'", finding.Message, StringComparison.Ordinal);
        Assert.Contains("requires at least 2", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReplicaFloor_IsSilentInNonProductionEnvironments()
    {
        var context = CheckTestHarness.Context(TestGraph.FromFixture(Bad), environment: "dev");
        var result = CheckTestHarness.Run(context);

        Assert.DoesNotContain(result.Findings, f => f.CheckId == "CP-REL-005");
        Assert.DoesNotContain(result.Passed, p => p.CheckId == "CP-REL-005");
    }

    [Fact]
    public void MissingPodDisruptionBudget_IsReported()
    {
        var finding = Assert.Single(CheckTestHarness.FindingsFor("CP-REL-006", Bad));

        Assert.Equal("Deployment/member-api", finding.Resource?.Key);
        Assert.True(CheckTestHarness.Passed("CP-REL-006", Good));
    }

    [Fact]
    public void RecreateStrategy_IsReported()
    {
        var finding = Assert.Single(CheckTestHarness.FindingsFor("CP-REL-007", Bad));

        Assert.Equal("spec.strategy.type", finding.YamlPath);
        Assert.Contains("Recreate", finding.Message, StringComparison.Ordinal);
        Assert.True(CheckTestHarness.Passed("CP-REL-007", Good));
    }

    [Fact]
    public void MissingStartupProbe_IsOnlyReported_WhenThereIsAnInitContainer()
    {
        var finding = Assert.Single(CheckTestHarness.FindingsFor("CP-REL-008", "reliability/startup-probe-bad.yaml"));

        Assert.Equal(Severity.Info, finding.Severity);
        Assert.Equal("spec.template.spec.containers[0]", finding.YamlPath);

        Assert.True(CheckTestHarness.Passed("CP-REL-008", "reliability/startup-probe-good.yaml"));
        Assert.True(CheckTestHarness.Passed("CP-REL-008", Bad));
    }

    [Fact]
    public void CronJobWithoutConcurrencyOrHistoryLimits_IsReported()
    {
        var findings = CheckTestHarness.FindingsFor("CP-REL-009", "reliability/cronjob-bad.yaml");

        Assert.Equal(2, findings.Count);
        Assert.Contains(findings, f => f.YamlPath == "spec.concurrencyPolicy");
        Assert.Contains(findings, f => f.YamlPath == "spec.failedJobsHistoryLimit");
        Assert.All(findings, f => Assert.Equal("CronJob/nightly-export", f.Resource?.Key));

        Assert.True(CheckTestHarness.Passed("CP-REL-009", "reliability/cronjob-good.yaml"));
    }

    [Fact]
    public void CronJobCheck_DoesNotRun_OnAChartWithNoCronJob()
    {
        var result = CheckTestHarness.Run(CheckTestHarness.Context(TestGraph.FromFixture(Good)));

        Assert.DoesNotContain(result.Findings, f => f.CheckId == "CP-REL-009");
        Assert.DoesNotContain(result.Passed, p => p.CheckId == "CP-REL-009");
    }

    [Fact]
    public void SingleReplicaWithoutAutoscaler_IsReported()
    {
        var finding = Assert.Single(CheckTestHarness.FindingsFor("CP-REL-010", Bad));

        Assert.Equal(Severity.Info, finding.Severity);
        Assert.Equal("spec.replicas", finding.YamlPath);

        Assert.True(CheckTestHarness.Passed("CP-REL-010", "reliability/autoscaler-good.yaml"));
    }
}
