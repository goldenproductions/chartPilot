using System.Text.RegularExpressions;
using ChartPilot.Core.Checks;

namespace ChartPilot.Core.Tests.Checks;

public class CheckCatalogTests
{
    private static readonly Regex IdFormat = new(@"^CP-(REL|SEC|NET|CERT|OBS|GOV)-\d{3}$", RegexOptions.CultureInvariant);

    private readonly CheckCatalog _catalog = CheckTestHarness.Catalog;

    [Fact]
    public void EveryRuleIdFollowsTheContractedFormat()
        => Assert.All(_catalog.Descriptors, d => Assert.Matches(IdFormat, d.Id));

    [Fact]
    public void RuleIdsAreUnique()
        => Assert.Equal(
            _catalog.Descriptors.Count,
            _catalog.Descriptors.Select(d => d.Id).Distinct(StringComparer.Ordinal).Count());

    [Fact]
    public void CatalogIsSortedById()
        => Assert.Equal(
            _catalog.Descriptors.Select(d => d.Id).OrderBy(id => id, StringComparer.Ordinal).ToArray(),
            _catalog.Descriptors.Select(d => d.Id).ToArray());

    [Fact]
    public void EveryFamilyIsPresentWithItsFullNumberRange()
    {
        AssertRange("CP-REL", 1, 10);
        AssertRange("CP-SEC", 1, 13);
        AssertRange("CP-NET", 1, 8);
        AssertRange("CP-CERT", 1, 5);
        AssertRange("CP-OBS", 1, 4);
        AssertRange("CP-GOV", 1, 8);

        void AssertRange(string prefix, int from, int to)
        {
            for (var i = from; i <= to; i++)
            {
                var id = $"{prefix}-{i:000}";
                Assert.True(_catalog.Find(id) is not null, $"{id} is missing from the catalog.");
            }
        }
    }

    [Fact]
    public void EveryDescriptorCarriesARationaleAndConcreteRemediation()
        => Assert.All(_catalog.Descriptors, d =>
        {
            Assert.False(string.IsNullOrWhiteSpace(d.Title), $"{d.Id} has no title.");
            Assert.False(string.IsNullOrWhiteSpace(d.Rationale), $"{d.Id} has no rationale.");
            Assert.False(string.IsNullOrWhiteSpace(d.Remediation), $"{d.Id} has no remediation.");
            Assert.True(d.Rationale.Length > 40, $"{d.Id} has a rationale that explains nothing.");
        });

    [Fact]
    public void CategoryMatchesTheFamilyPrefix_ForTheUnambiguousFamilies()
        => Assert.All(_catalog.Descriptors, d =>
        {
            if (d.Id.StartsWith("CP-SEC-", StringComparison.Ordinal))
            {
                Assert.Equal(CheckCategory.Security, d.Category);
            }
            else if (d.Id.StartsWith("CP-REL-", StringComparison.Ordinal))
            {
                Assert.Equal(CheckCategory.Reliability, d.Category);
            }
            else if (d.Id.StartsWith("CP-GOV-", StringComparison.Ordinal))
            {
                Assert.Equal(CheckCategory.Governance, d.Category);
            }
            else if (d.Id.StartsWith("CP-CERT-", StringComparison.Ordinal)
                     || d.Id.StartsWith("CP-OBS-", StringComparison.Ordinal))
            {
                Assert.Equal(CheckCategory.Operability, d.Category);
            }
        });

    [Fact]
    public void FindIsCaseInsensitiveAndReturnsNullForUnknownIds()
    {
        Assert.NotNull(_catalog.Find("cp-sec-001"));
        Assert.Null(_catalog.Find("CP-SEC-999"));
    }

    [Fact]
    public void DuplicateIdsAreRejected()
    {
        var duplicate = _catalog.Checks.First();

        var exception = Assert.Throws<InvalidOperationException>(
            () => new CheckCatalog([duplicate, duplicate]));

        Assert.Contains("Duplicate check id", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DiscoveryFindsEveryRuleInTheAssembly()
        => Assert.Equal(CheckCatalog.DiscoverCheckTypes().Count, _catalog.Checks.Count);
}
