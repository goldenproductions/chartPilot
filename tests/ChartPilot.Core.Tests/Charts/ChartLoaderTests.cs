using ChartPilot.Core.Charts;

namespace ChartPilot.Core.Tests.Charts;

public class ChartLoaderTests
{
    private readonly ChartLoader _loader = new();

    [Fact]
    public void Reads_the_metadata_from_Chart_yaml()
    {
        var chart = _loader.Load(ChartFixtures.Dir(ChartFixtures.FullChart));

        Assert.Equal("fixture-api", chart.Name);
        Assert.Equal("1.4.2", chart.Version);
        Assert.Equal("3.1.0", chart.AppVersion);
        Assert.Equal("A fixture chart used by the ChartLoader tests.", chart.Description);
        Assert.Equal("application", chart.Type);
        Assert.Equal(">=1.28.0-0", chart.KubeVersion);
        Assert.Equal(ChartFixtures.Dir(ChartFixtures.FullChart), chart.ChartPath);
    }

    [Fact]
    public void Reads_maintainers_and_skips_entries_without_a_name()
    {
        var chart = _loader.Load(ChartFixtures.Dir(ChartFixtures.FullChart));

        Assert.Equal(2, chart.Maintainers.Count);

        var first = chart.Maintainers[0];
        Assert.Equal("Platform Team", first.Name);
        Assert.Equal("platform@example.com", first.Email);
        Assert.Equal("https://example.com/teams/platform", first.Url);

        var second = chart.Maintainers[1];
        Assert.Equal("Second Maintainer", second.Name);
        Assert.Null(second.Email);
        Assert.Null(second.Url);
    }

    [Fact]
    public void Reads_dependencies_including_condition_repository_and_tags()
    {
        var chart = _loader.Load(ChartFixtures.Dir(ChartFixtures.FullChart));

        Assert.Equal(2, chart.Dependencies.Count);

        var redis = chart.Dependencies[0];
        Assert.Equal("redis", redis.Name);
        Assert.Equal("19.6.4", redis.Version);
        Assert.Equal("https://charts.example.com/bitnami", redis.Repository);
        Assert.Equal("redis.enabled", redis.Condition);
        Assert.Equal(new[] { "cache", "storage" }, redis.Tags);
    }

    [Fact]
    public void Distinguishes_a_pinned_dependency_from_a_range()
    {
        var chart = _loader.Load(ChartFixtures.Dir(ChartFixtures.FullChart));

        var redis = chart.Dependencies.Single(d => d.Name == "redis");
        var mongodb = chart.Dependencies.Single(d => d.Name == "mongodb");

        Assert.True(redis.IsVersionPinned);
        Assert.Equal("^15.0.0", mongodb.Version);
        Assert.False(mongodb.IsVersionPinned);
        Assert.Null(mongodb.Condition);
        Assert.Empty(mongodb.Tags);
    }

    [Fact]
    public void Discovers_the_default_values_file_and_every_environment_overlay()
    {
        var chart = _loader.Load(ChartFixtures.Dir(ChartFixtures.FullChart));

        Assert.Equal(3, chart.ValuesFiles.Count);

        var defaults = chart.ValuesFiles[0];
        Assert.Equal("values.yaml", defaults.FileName);
        Assert.True(defaults.IsDefault);
        Assert.Null(defaults.EnvironmentName);
        Assert.True(File.Exists(defaults.FullPath));

        Assert.Equal(new[] { "dev", "prod" }, chart.ValuesFiles.Skip(1).Select(f => f.EnvironmentName));
        Assert.All(chart.ValuesFiles.Skip(1), f => Assert.False(f.IsDefault));
    }

    [Fact]
    public void Ignores_files_that_only_look_like_values_files()
    {
        var chart = _loader.Load(ChartFixtures.Dir(ChartFixtures.FullChart));

        Assert.DoesNotContain(chart.ValuesFiles, f => f.FileName.EndsWith(".txt", StringComparison.Ordinal));
        Assert.DoesNotContain(chart.ValuesFiles, f => f.FileName == "values.schema.json");
    }

    [Fact]
    public void Detects_the_values_schema_and_keeps_its_raw_json()
    {
        var chart = _loader.Load(ChartFixtures.Dir(ChartFixtures.FullChart));

        Assert.True(chart.HasValuesSchema);
        Assert.NotNull(chart.ValuesSchemaJson);
        Assert.Contains("\"replicaCount\"", chart.ValuesSchemaJson);
    }

    [Fact]
    public void Detects_the_suppressions_file_by_presence_alone()
    {
        var chart = _loader.Load(ChartFixtures.Dir(ChartFixtures.FullChart));

        Assert.True(chart.HasSuppressionsFile);
    }

    [Fact]
    public void Enumerates_templates_recursively_with_forward_slashed_relative_paths()
    {
        var chart = _loader.Load(ChartFixtures.Dir(ChartFixtures.FullChart));

        Assert.Equal(
            new[]
            {
                "templates/_helpers.tpl",
                "templates/deployment.yaml",
                "templates/nested/service.yaml",
                "templates/rbac.yaml"
            },
            chart.Templates.Select(t => t.RelativePath));

        Assert.DoesNotContain(chart.Templates, t => t.RelativePath.EndsWith("NOTES.txt", StringComparison.Ordinal));
    }

    [Fact]
    public void Scans_each_template_for_the_kinds_it_can_emit()
    {
        var chart = _loader.Load(ChartFixtures.Dir(ChartFixtures.FullChart));

        var deployment = chart.Templates.Single(t => t.RelativePath == "templates/deployment.yaml");
        var helpers = chart.Templates.Single(t => t.RelativePath == "templates/_helpers.tpl");
        var rbac = chart.Templates.Single(t => t.RelativePath == "templates/rbac.yaml");

        Assert.Equal(new[] { "Deployment" }, deployment.DetectedKinds);
        Assert.Empty(helpers.DetectedKinds);
        Assert.Equal(new[] { "ClusterRole", "ClusterRoleBinding" }, rbac.DetectedKinds);
    }

    [Fact]
    public void Unions_the_detected_kinds_across_every_template()
    {
        var chart = _loader.Load(ChartFixtures.Dir(ChartFixtures.FullChart));

        Assert.Equal(
            new[] { "ClusterRole", "ClusterRoleBinding", "Deployment", "Service" },
            chart.DetectedKinds);
    }

    [Fact]
    public void A_minimal_chart_has_no_appVersion_no_maintainers_and_no_templates()
    {
        var chart = _loader.Load(ChartFixtures.Dir(ChartFixtures.MinimalChart));

        Assert.Equal("minimal", chart.Name);
        Assert.Equal("0.1.0", chart.Version);
        Assert.Null(chart.AppVersion);
        Assert.Null(chart.Description);
        Assert.Null(chart.KubeVersion);
        Assert.Empty(chart.Maintainers);
        Assert.Empty(chart.Dependencies);
        Assert.Empty(chart.ValuesFiles);
        Assert.Empty(chart.Templates);
        Assert.Empty(chart.DetectedKinds);
        Assert.False(chart.HasValuesSchema);
        Assert.Null(chart.ValuesSchemaJson);
        Assert.False(chart.HasSuppressionsFile);
    }

    [Fact]
    public void A_directory_without_a_Chart_yaml_cannot_be_loaded()
    {
        var path = ChartFixtures.Dir(ChartFixtures.NotAChart);

        var exception = Assert.Throws<ChartLoadException>(() => _loader.Load(path));

        Assert.Contains("Chart.yaml", exception.Message, StringComparison.Ordinal);
        Assert.Equal(path, exception.ChartPath);
    }

    [Fact]
    public void A_missing_directory_cannot_be_loaded()
    {
        var path = ChartFixtures.Dir("no-such-chart");

        var exception = Assert.Throws<ChartLoadException>(() => _loader.Load(path));

        Assert.Contains("does not exist", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unparseable_Chart_yaml_cannot_be_loaded()
    {
        var exception = Assert.Throws<ChartLoadException>(
            () => _loader.Load(ChartFixtures.Dir(ChartFixtures.BrokenChart)));

        Assert.Contains("not valid YAML", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void An_empty_path_cannot_be_loaded(string path)
    {
        Assert.Throws<ChartLoadException>(() => _loader.Load(path));
    }

    [Fact]
    public void IsChartDirectory_is_true_only_for_directories_holding_a_Chart_yaml()
    {
        Assert.True(_loader.IsChartDirectory(ChartFixtures.Dir(ChartFixtures.FullChart)));
        Assert.True(_loader.IsChartDirectory(ChartFixtures.Dir(ChartFixtures.MinimalChart)));
        Assert.False(_loader.IsChartDirectory(ChartFixtures.Dir(ChartFixtures.NotAChart)));
        Assert.False(_loader.IsChartDirectory(ChartFixtures.Dir("no-such-chart")));
        Assert.False(_loader.IsChartDirectory(string.Empty));
    }
}
