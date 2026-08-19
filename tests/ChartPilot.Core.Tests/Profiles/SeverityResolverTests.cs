using ChartPilot.Core.Checks;
using ChartPilot.Core.Profiles;

namespace ChartPilot.Core.Tests.Profiles;

public class SeverityResolverTests
{
    private readonly SeverityResolver _resolver = new();

    private static CheckDescriptor Descriptor(string id, Severity severity, CheckCategory category = CheckCategory.Security)
        => new(id, "title", category, severity, "rationale", "remediation");

    [Fact]
    public void TheDefaultSeverityIsUsedWhenNothingPromotesIt()
    {
        var resolved = _resolver.Resolve(
            Descriptor("CP-SEC-008", Severity.Warning),
            BuiltInProfiles.Default,
            DataClassification.Unclassified);

        Assert.Equal(Severity.Warning, resolved);
    }

    [Fact]
    public void AMandatoryProfileRequirementPromotesToCritical()
    {
        var profile = BuiltInProfiles.Default with
        {
            Requirements = new ProfileRequirements(RequireNetworkPolicy: true)
        };

        var resolved = _resolver.Resolve(
            Descriptor("CP-SEC-008", Severity.Warning), profile, DataClassification.Unclassified);

        Assert.Equal(Severity.Critical, resolved);
    }

    [Fact]
    public void AMinimumReplicaCountPromotesTheReplicaRule()
    {
        var profile = BuiltInProfiles.Default with { Requirements = new ProfileRequirements(MinReplicas: 2) };

        Assert.Equal(
            Severity.Critical,
            _resolver.Resolve(Descriptor("CP-REL-005", Severity.Warning), profile, DataClassification.Unclassified));
    }

    [Fact]
    public void ForbiddingPublicIngressPromotesThePublicRouteRules()
    {
        var profile = BuiltInProfiles.Default with
        {
            Requirements = new ProfileRequirements(AllowPublicIngress: false)
        };

        Assert.Equal(
            Severity.Critical,
            _resolver.Resolve(Descriptor("CP-NET-001", Severity.Warning), profile, DataClassification.Unclassified));
    }

    [Fact]
    public void SensitivePersonalDataPromotesTheProtectedSetToCritical()
    {
        foreach (var id in new[] { "CP-NET-003", "CP-SEC-008", "CP-NET-002", "CP-SEC-007", "CP-REL-004" })
        {
            var resolved = _resolver.Resolve(
                Descriptor(id, Severity.Info),
                BuiltInProfiles.Default,
                DataClassification.SensitivePersonalData);

            Assert.Equal(Severity.Critical, resolved);
        }
    }

    [Fact]
    public void ConfidentialDataPromotesInfoToWarningButNoFurther()
    {
        Assert.Equal(
            Severity.Warning,
            _resolver.Resolve(
                Descriptor("CP-OBS-003", Severity.Info), BuiltInProfiles.Default, DataClassification.Confidential));

        Assert.Equal(
            Severity.Warning,
            _resolver.Resolve(
                Descriptor("CP-OBS-001", Severity.Warning), BuiltInProfiles.Default, DataClassification.Confidential));
    }

    [Fact]
    public void PromotionNeverDemotes()
    {
        // A Critical default stays Critical even though nothing in the profile mandates it.
        Assert.Equal(
            Severity.Critical,
            _resolver.Resolve(
                Descriptor("CP-SEC-002", Severity.Critical), BuiltInProfiles.Default, DataClassification.Public));
    }

    [Fact]
    public void AnExplicitOverrideWinsOverEveryPromotion()
    {
        var profile = BuiltInProfiles.Default with
        {
            Requirements = new ProfileRequirements(RequireNetworkPolicy: true),
            SeverityOverrides = new Dictionary<string, Severity>(StringComparer.OrdinalIgnoreCase)
            {
                ["CP-SEC-008"] = Severity.Info
            }
        };

        var resolved = _resolver.Resolve(
            Descriptor("CP-SEC-008", Severity.Warning), profile, DataClassification.SensitivePersonalData);

        Assert.Equal(Severity.Info, resolved);
    }

    [Fact]
    public void TheResolutionOrderIsDefaultThenProfileThenClassificationThenOverride()
    {
        var descriptor = Descriptor("CP-REL-004", Severity.Warning, CheckCategory.Reliability);

        // 1. default
        Assert.Equal(
            Severity.Warning,
            _resolver.Resolve(descriptor, BuiltInProfiles.Default, DataClassification.Unclassified));

        // 2. profile promotion
        var mandatory = BuiltInProfiles.Default with
        {
            Requirements = new ProfileRequirements(RequireResourceLimits: true)
        };
        Assert.Equal(Severity.Critical, _resolver.Resolve(descriptor, mandatory, DataClassification.Unclassified));

        // 3. classification promotion, without the profile requiring anything
        Assert.Equal(
            Severity.Critical,
            _resolver.Resolve(descriptor, BuiltInProfiles.Default, DataClassification.SensitivePersonalData));

        // 4. override beats both
        var overridden = mandatory with
        {
            SeverityOverrides = new Dictionary<string, Severity>(StringComparer.OrdinalIgnoreCase)
            {
                ["CP-REL-004"] = Severity.Warning
            }
        };
        Assert.Equal(
            Severity.Warning,
            _resolver.Resolve(descriptor, overridden, DataClassification.SensitivePersonalData));
    }

    [Fact]
    public void TheRequirementMapIsQueryableOnItsOwn()
    {
        Assert.True(SeverityResolver.IsMandatedBy(
            new ProfileRequirements(RequireReadinessProbe: true), "CP-REL-001"));

        Assert.False(SeverityResolver.IsMandatedBy(
            new ProfileRequirements(RequireReadinessProbe: true), "CP-REL-002"));
    }
}
