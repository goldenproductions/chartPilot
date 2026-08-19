using ChartPilot.Core.Checks;
using ChartPilot.Core.Profiles;
using ChartPilot.Core.Tests.Checks;

namespace ChartPilot.Core.Tests.Profiles;

public class ProfileStoreTests
{
    private static string FixturePath(string name)
        => Path.Combine(AppContext.BaseDirectory, "Fixtures", "Profiles", name);

    [Fact]
    public void EveryProfileTheSpecNamesIsShipped()
    {
        var store = new ProfileStore();

        foreach (var id in new[]
                 {
                     "default", "public-web-service", "internal-api", "sensitive-member-data-service",
                     "batch-job", "legacy-integration-service", "sandbox-service"
                 })
        {
            Assert.True(store.Get(id) is not null, $"Profile '{id}' is missing.");
        }

        Assert.Equal(7, store.Profiles.Count);
    }

    [Fact]
    public void TheDefaultProfileRequiresNothing()
    {
        var store = new ProfileStore();

        Assert.Equal("default", store.Default.Id);
        Assert.Equal(new ProfileRequirements(), store.Default.Requirements);
        Assert.Empty(store.Default.DisabledChecks);
        Assert.Empty(store.Default.SeverityOverrides);
    }

    [Fact]
    public void GetIsCaseInsensitiveAndReturnsNullForUnknownProfiles()
    {
        var store = new ProfileStore();

        Assert.NotNull(store.Get("SANDBOX-SERVICE"));
        Assert.Null(store.Get("no-such-profile"));
    }

    [Fact]
    public void SensitiveMemberDataServiceCarriesTheRequirementsTheSpecMandates()
    {
        var requirements = new ProfileStore().Get("sensitive-member-data-service")!.Requirements;

        Assert.True(requirements.RequireNetworkPolicy);
        Assert.True(requirements.RequireMtls);
        Assert.True(requirements.RequireAuthorizationPolicy);
        Assert.True(requirements.RequireResourceLimits);
        Assert.True(requirements.RequireNonRoot);
        Assert.True(requirements.DisallowInlineSecrets);
        Assert.True(requirements.DisallowLatestTag);
        Assert.False(requirements.AllowPublicIngress);
        Assert.Equal(2, requirements.MinReplicas);
    }

    [Fact]
    public void SandboxServiceIsPermissive()
    {
        var sandbox = new ProfileStore().Get("sandbox-service")!;

        Assert.Equal(new ProfileRequirements(), sandbox.Requirements with { AllowPublicIngress = true, MinReplicas = 1 });
        Assert.NotEmpty(sandbox.DisabledChecks);
        Assert.True(sandbox.Deductions.Critical < BuiltInProfiles.Default.Deductions.Critical);
    }

    [Fact]
    public void EveryProfilesWeightsSumToOne()
        => Assert.All(new ProfileStore().Profiles, profile =>
        {
            var sum = profile.Weights.Security + profile.Weights.Reliability
                      + profile.Weights.Operability + profile.Weights.Governance;

            Assert.True(Math.Abs(sum - 1.0) < 0.0001, $"Profile '{profile.Id}' weights sum to {sum}.");
        });

    [Fact]
    public void EveryDisabledCheckIdExistsInTheCatalog()
    {
        var catalog = CheckTestHarness.Catalog;

        foreach (var profile in new ProfileStore().Profiles)
        {
            foreach (var id in profile.DisabledChecks)
            {
                Assert.True(catalog.Find(id) is not null, $"Profile '{profile.Id}' disables unknown check '{id}'.");
            }

            foreach (var id in profile.SeverityOverrides.Keys)
            {
                Assert.True(catalog.Find(id) is not null, $"Profile '{profile.Id}' overrides unknown check '{id}'.");
            }
        }
    }

    [Fact]
    public void AProfileCanBeLoadedFromAYamlFile()
    {
        var store = new ProfileStore();

        var profile = store.LoadFromFile(FixturePath("custom-profile.yaml"));

        Assert.Equal("member-platform-strict", profile.Id);
        Assert.Equal("Member platform (strict)", profile.Name);
        Assert.True(profile.Requirements.RequireMtls);
        Assert.False(profile.Requirements.AllowPublicIngress);
        Assert.Equal(3, profile.Requirements.MinReplicas);
        Assert.Equal(Severity.Info, profile.SeverityOverrides["CP-OBS-003"]);
        Assert.Equal(Severity.Warning, profile.SeverityOverrides["CP-CERT-004"]);
        Assert.Equal(Severity.Critical, profile.SeverityOverrides["CP-SEC-002"]);
        Assert.Equal(new[] { "CP-REL-009", "CP-OBS-004" }, profile.DisabledChecks);
        Assert.Equal(0.4, profile.Weights.Security);
        Assert.Equal(30, profile.Deductions.Critical);
        Assert.Equal(2, profile.Deductions.Info);
    }

    [Fact]
    public void ALoadedProfileCanBeAddedToTheCatalogAndFetchedBack()
    {
        var store = new ProfileStore();

        store.Add(store.LoadFromFile(FixturePath("custom-profile.yaml")));

        Assert.Equal(8, store.Profiles.Count);
        Assert.NotNull(store.Get("member-platform-strict"));

        // Re-adding replaces rather than duplicates.
        store.Add(store.LoadFromFile(FixturePath("custom-profile.yaml")));
        Assert.Equal(8, store.Profiles.Count);
    }

    [Fact]
    public void AMinimalProfileFallsBackToEveryDefault()
    {
        var profile = new ProfileStore().LoadFromFile(FixturePath("minimal-profile.yaml"));

        Assert.Equal("bare-minimum", profile.Id);
        Assert.Equal("bare-minimum", profile.Name);
        Assert.Equal(new ProfileRequirements(), profile.Requirements);
        Assert.Equal(new ScoreWeights(), profile.Weights);
        Assert.Equal(new SeverityDeductions(), profile.Deductions);
    }

    [Fact]
    public void AnUnknownSeverityInAProfileIsRejected()
    {
        var store = new ProfileStore();

        var exception = Assert.Throws<ProfileParseException>(
            () => store.LoadFromFile(FixturePath("bad-severity-profile.yaml")));

        Assert.Contains("catastrophic", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AProfileWithoutAnIdIsRejected()
        => Assert.Throws<ProfileParseException>(() => ProfileStore.Parse("name: nameless\n", "inline.yaml"));

    [Fact]
    public void AMissingProfileFileIsRejected()
        => Assert.Throws<ProfileParseException>(
            () => new ProfileStore().LoadFromFile(FixturePath("does-not-exist.yaml")));
}
