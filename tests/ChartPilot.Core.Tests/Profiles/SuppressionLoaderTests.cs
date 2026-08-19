using ChartPilot.Core.Profiles;

namespace ChartPilot.Core.Tests.Profiles;

public class SuppressionLoaderTests
{
    private const string Sample = """
        suppress:
          - id: CP-SEC-004
            resource: Deployment/legacy-importer
            reason: "Vendor image requires a writable root filesystem; tracked in PLAT-412"
            expires: 2026-12-01
          - id: CP-REL-006
            reason: "Single-instance batch importer, no HA requirement"
          - id: CP-SEC-002
            expires: 2027-01-01
          - id: CP-OBS-003
            reason: "Legacy port naming"
            expires: not-a-date
          - reason: "orphaned entry"
        """;

    [Fact]
    public void ValidEntriesAreParsedIncludingResourceScopeAndExpiry()
    {
        var result = SuppressionLoader.Parse(Sample, ".chartpilot.yaml");

        Assert.Equal(2, result.Suppressions.Count);

        var scoped = result.Suppressions[0];
        Assert.Equal("CP-SEC-004", scoped.Id);
        Assert.Equal("Deployment/legacy-importer", scoped.Resource);
        Assert.Equal(new DateOnly(2026, 12, 1), scoped.Expires);
        Assert.Contains("PLAT-412", scoped.Reason, StringComparison.Ordinal);

        var unscoped = result.Suppressions[1];
        Assert.Equal("CP-REL-006", unscoped.Id);
        Assert.Null(unscoped.Resource);
        Assert.Null(unscoped.Expires);
    }

    [Fact]
    public void AnEntryWithoutAReasonIsRejected()
    {
        var result = SuppressionLoader.Parse(Sample, ".chartpilot.yaml");

        Assert.DoesNotContain(result.Suppressions, s => s.Id == "CP-SEC-002");
        Assert.Contains(result.Rejected, r => r.Id == "CP-SEC-002" && r.Problem == "has no reason");
    }

    [Fact]
    public void AnUnparseableExpiryIsRejected()
    {
        var result = SuppressionLoader.Parse(Sample, ".chartpilot.yaml");

        Assert.DoesNotContain(result.Suppressions, s => s.Id == "CP-OBS-003");
        Assert.Contains(result.Rejected, r => r.Id == "CP-OBS-003" && r.Problem.Contains("not-a-date", StringComparison.Ordinal));
    }

    [Fact]
    public void AnEntryWithoutAnIdIsRejected()
    {
        var result = SuppressionLoader.Parse(Sample, ".chartpilot.yaml");

        Assert.Contains(result.Rejected, r => r.Problem == "has no check id");
    }

    [Fact]
    public void ExpiryUsesTheIsoDateFormat()
    {
        var result = SuppressionLoader.Parse(
            "suppress:\n  - id: CP-SEC-001\n    reason: temporary\n    expires: 2026-03-09\n", "x");

        Assert.Equal(new DateOnly(2026, 3, 9), Assert.Single(result.Suppressions).Expires);
    }

    [Fact]
    public void MalformedYamlYieldsNothingRatherThanThrowing()
        => Assert.Empty(SuppressionLoader.Parse("suppress: [ unterminated", "x").Suppressions);

    [Fact]
    public void AChartDirectoryWithNoSuppressionFileYieldsAnEmptyList()
    {
        var directory = CreateTempDirectory();

        try
        {
            Assert.Empty(new SuppressionLoader().Load(directory));
            Assert.Null(SuppressionLoader.ResolvePath(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void TheFileIsReadFromTheChartDirectory()
    {
        var directory = CreateTempDirectory();

        try
        {
            File.WriteAllText(Path.Combine(directory, SuppressionLoader.FileName), Sample);

            var loaded = new SuppressionLoader().Load(directory);

            Assert.Equal(2, loaded.Count);
            Assert.Equal("CP-SEC-004", loaded[0].Id);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ExpiryIsRelativeToTheSuppliedDate()
    {
        var suppression = Assert.Single(
            SuppressionLoader.Parse(
                "suppress:\n  - id: CP-SEC-001\n    reason: temporary\n    expires: 2026-06-01\n", "x").Suppressions);

        Assert.False(suppression.IsExpired(new DateOnly(2026, 6, 1)));
        Assert.True(suppression.IsExpired(new DateOnly(2026, 6, 2)));
    }

    private static string CreateTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "chartpilot-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
