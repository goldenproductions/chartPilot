using ChartPilot.Core.Checks;
using ChartPilot.Core.Checks.Guidance;

namespace ChartPilot.Core.Tests.Checks;

/// <summary>
/// The catalog of authored guidance has to keep pace with the catalog of rules. These tests are the
/// forcing function: adding a rule without writing its guidance fails the build, which is the only
/// thing that stops "some findings explain themselves and some do not" creeping back in.
/// </summary>
public sealed class GuidanceCatalogTests
{
    private static IReadOnlyList<CheckDescriptor> AllDescriptors()
        => CheckTestHarness.Catalog.Descriptors;

    [Fact]
    public void Every_registered_check_has_authored_guidance()
    {
        var missing = AllDescriptors()
            .Where(d => GuidanceCatalog.For(d.Id) is null)
            .Select(d => $"{d.Id} ({d.Title})")
            .ToList();

        Assert.True(
            missing.Count == 0,
            "These checks have no entry in GuidanceCatalog, so their findings would offer no options:\n  "
            + string.Join("\n  ", missing));
    }

    [Fact]
    public void Guidance_is_not_authored_for_checks_that_do_not_exist()
    {
        var known = AllDescriptors().Select(d => d.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var orphans = GuidanceCatalog.CoveredCheckIds
            .Where(id => !known.Contains(id))
            .ToList();

        Assert.True(
            orphans.Count == 0,
            "Guidance exists for check ids that are not registered - a rule was renamed or removed:\n  "
            + string.Join("\n  ", orphans));
    }

    [Fact]
    public void Every_check_offers_at_least_one_option()
    {
        foreach (var descriptor in AllDescriptors())
        {
            var guidance = GuidanceCatalog.For(descriptor.Id);
            Assert.NotNull(guidance);
            Assert.NotEmpty(guidance!.Options);
        }
    }

    [Fact]
    public void Exactly_one_option_per_check_is_recommended()
    {
        foreach (var descriptor in AllDescriptors())
        {
            var guidance = GuidanceCatalog.For(descriptor.Id)!;
            var recommended = guidance.Options.Count(o => o.IsRecommended);

            Assert.True(
                recommended == 1,
                $"{descriptor.Id} marks {recommended} options as recommended; a reader in a hurry needs "
                + "exactly one place to start.");
        }
    }

    [Fact]
    public void The_recommended_option_is_listed_first()
    {
        foreach (var descriptor in AllDescriptors())
        {
            var guidance = GuidanceCatalog.For(descriptor.Id)!;

            Assert.True(
                guidance.Options[0].IsRecommended,
                $"{descriptor.Id} does not list its recommended option first.");
        }
    }

    [Fact]
    public void Every_option_is_complete()
    {
        foreach (var descriptor in AllDescriptors())
        {
            var guidance = GuidanceCatalog.For(descriptor.Id)!;

            Assert.False(string.IsNullOrWhiteSpace(guidance.WhatItMeans), $"{descriptor.Id} has no WhatItMeans.");

            foreach (var option in guidance.Options)
            {
                Assert.False(string.IsNullOrWhiteSpace(option.Title), $"{descriptor.Id}: option has no title.");
                Assert.False(string.IsNullOrWhiteSpace(option.Summary), $"{descriptor.Id}/{option.Title}: no summary.");
                Assert.False(string.IsNullOrWhiteSpace(option.Yaml), $"{descriptor.Id}/{option.Title}: no yaml.");

                // An option without a stated cost is not an option, it is an instruction — the reader
                // cannot choose between two things when only one of them admits a downside.
                Assert.False(
                    string.IsNullOrWhiteSpace(option.Tradeoff),
                    $"{descriptor.Id}/{option.Title}: no trade-off stated.");
            }
        }
    }

    [Fact]
    public void Guidance_does_not_repeat_the_rules_own_rationale_verbatim()
    {
        foreach (var descriptor in AllDescriptors())
        {
            var guidance = GuidanceCatalog.For(descriptor.Id)!;

            Assert.False(
                string.Equals(guidance.WhatItMeans.Trim(), descriptor.Rationale.Trim(), StringComparison.OrdinalIgnoreCase),
                $"{descriptor.Id}: WhatItMeans duplicates Rationale. The rationale says why the rule exists; "
                + "WhatItMeans should say what the finding means for this reader.");
        }
    }
}
