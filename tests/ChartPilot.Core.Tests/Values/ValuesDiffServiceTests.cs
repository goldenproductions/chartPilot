using ChartPilot.Core.Values;

namespace ChartPilot.Core.Tests.Values;

public class ValuesDiffServiceTests
{
    private readonly ValuesDiffService _diff = new();

    private static ValuesDocument Doc(string yaml, string name) => ValuesDocument.Parse(yaml, name);

    private static ValuesDiffRow Row(ValuesDiffResult result, string path)
        => Assert.Single(result.Rows, r => r.Path == path);

    [Fact]
    public void Two_documents_are_compared_path_by_path()
    {
        var dev = Doc("replicaCount: 1\nimage:\n  tag: \"1.12.0\"\n", "values-dev.yaml");
        var prod = Doc("replicaCount: 3\nimage:\n  tag: \"1.12.0\"\n", "values-prod.yaml");

        var result = _diff.Diff([dev, prod], differencesOnly: false);

        Assert.Equal(new[] { "values-dev.yaml", "values-prod.yaml" }, result.Sources);
        Assert.Equal(new[] { "image.tag", "replicaCount" }, result.Rows.Select(r => r.Path));

        var replicas = Row(result, "replicaCount");
        Assert.True(replicas.IsDifferent);
        Assert.Equal("1", replicas.Cells[0].Value);
        Assert.Equal("3", replicas.Cells[1].Value);

        Assert.False(Row(result, "image.tag").IsDifferent);
    }

    [Fact]
    public void A_path_missing_from_one_document_is_marked_absent_and_different()
    {
        var dev = Doc("replicaCount: 1\n", "values-dev.yaml");
        var test = Doc("replicaCount: 1\n", "values-test.yaml");
        var prod = Doc("replicaCount: 1\npodDisruptionBudget:\n  minAvailable: 2\n", "values-prod.yaml");

        var result = _diff.Diff([dev, test, prod], differencesOnly: false);

        var row = Row(result, "podDisruptionBudget.minAvailable");

        Assert.True(row.IsDifferent);
        Assert.False(row.Cells[0].Present);
        Assert.Null(row.Cells[0].Value);
        Assert.False(row.Cells[1].Present);
        Assert.True(row.Cells[2].Present);
        Assert.Equal("2", row.Cells[2].Value);
        Assert.Equal("values-prod.yaml", row.Cells[2].Source);
    }

    [Fact]
    public void Four_documents_are_supported()
    {
        var documents = new[]
        {
            Doc("logLevel: info\n", "values.yaml"),
            Doc("logLevel: debug\n", "values-dev.yaml"),
            Doc("logLevel: info\n", "values-test.yaml"),
            Doc("logLevel: warning\n", "values-prod.yaml")
        };

        var result = _diff.Diff(documents, differencesOnly: false);

        var row = Assert.Single(result.Rows);

        Assert.Equal(4, result.Sources.Count);
        Assert.Equal(4, row.Cells.Count);
        Assert.True(row.IsDifferent);
        Assert.Equal(new[] { "info", "debug", "info", "warning" }, row.Cells.Select(c => c.Value));
        Assert.All(row.Cells, c => Assert.True(c.Present));
    }

    [Fact]
    public void differencesOnly_drops_the_rows_every_source_agrees_on()
    {
        var dev = Doc("replicaCount: 1\nimage:\n  tag: \"1.12.0\"\n", "values-dev.yaml");
        var prod = Doc("replicaCount: 3\nimage:\n  tag: \"1.12.0\"\n", "values-prod.yaml");

        var result = _diff.Diff([dev, prod], differencesOnly: true);

        var row = Assert.Single(result.Rows);
        Assert.Equal("replicaCount", row.Path);
    }

    [Fact]
    public void Identical_documents_produce_no_differences()
    {
        var a = Doc("replicaCount: 2\nhosts:\n  - a.example.com\n", "a.yaml");
        var b = Doc("replicaCount: 2\nhosts:\n  - a.example.com\n", "b.yaml");

        var all = _diff.Diff([a, b], differencesOnly: false);
        var onlyDifferences = _diff.Diff([a, b], differencesOnly: true);

        Assert.Equal(2, all.Rows.Count);
        Assert.All(all.Rows, r => Assert.False(r.IsDifferent));
        Assert.Empty(onlyDifferences.Rows);
    }

    [Fact]
    public void Sequence_elements_are_compared_by_index()
    {
        var a = Doc("hosts:\n  - a.example.com\n  - shared.example.com\n", "a.yaml");
        var b = Doc("hosts:\n  - b.example.com\n  - shared.example.com\n", "b.yaml");

        var result = _diff.Diff([a, b], differencesOnly: true);

        var row = Assert.Single(result.Rows);
        Assert.Equal("hosts[0]", row.Path);
    }

    [Fact]
    public void A_single_document_produces_rows_that_are_never_different()
    {
        var only = Doc("replicaCount: 2\n", "values.yaml");

        var result = _diff.Diff([only], differencesOnly: false);

        var row = Assert.Single(result.Rows);
        Assert.False(row.IsDifferent);
        Assert.True(row.Cells[0].Present);
    }

    [Fact]
    public void No_documents_produce_an_empty_result()
    {
        var result = _diff.Diff([], differencesOnly: false);

        Assert.Empty(result.Sources);
        Assert.Empty(result.Rows);
    }

    [Fact]
    public void A_yaml_null_is_present_with_a_null_value_and_differs_from_an_absent_path()
    {
        var withNull = Doc("nodeSelector: null\n", "a.yaml");
        var without = Doc("replicaCount: 1\n", "b.yaml");

        var result = _diff.Diff([withNull, without], differencesOnly: false);

        var row = Row(result, "nodeSelector");

        Assert.True(row.Cells[0].Present);
        Assert.Null(row.Cells[0].Value);
        Assert.False(row.Cells[1].Present);
        Assert.True(row.IsDifferent);
    }
}
