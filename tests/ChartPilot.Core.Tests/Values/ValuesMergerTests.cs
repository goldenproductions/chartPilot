using ChartPilot.Core.Values;

namespace ChartPilot.Core.Tests.Values;

public class ValuesMergerTests
{
    private readonly ValuesMerger _merger = new();

    private static ValuesDocument Doc(string yaml, string name) => ValuesDocument.Parse(yaml, name);

    [Fact]
    public void Mappings_merge_key_by_key_with_the_later_layer_winning()
    {
        var baseValues = Doc(
            """
            replicaCount: 1
            image:
              repository: ghcr.io/example/member-api
              tag: "1.12.0"
              pullPolicy: IfNotPresent
            """,
            "values.yaml");

        var overlay = Doc(
            """
            replicaCount: 3
            image:
              tag: "1.13.0"
            """,
            "values-prod.yaml");

        var merged = _merger.Merge([baseValues, overlay], "<merged>");

        Assert.Equal("<merged>", merged.SourceName);
        Assert.Equal(3, merged.GetInt("replicaCount"));
        Assert.Equal("1.13.0", merged.GetString("image.tag"));
        Assert.Equal("ghcr.io/example/member-api", merged.GetString("image.repository"));
        Assert.Equal("IfNotPresent", merged.GetString("image.pullPolicy"));
    }

    [Fact]
    public void Sequences_are_replaced_wholesale_and_never_concatenated()
    {
        var baseValues = Doc(
            """
            hosts:
              - a.example.com
              - b.example.com
            """,
            "values.yaml");

        var overlay = Doc(
            """
            hosts:
              - prod.example.com
            """,
            "values-prod.yaml");

        var merged = _merger.Merge([baseValues, overlay], "<merged>");

        Assert.Equal("prod.example.com", merged.GetString("hosts[0]"));
        Assert.Null(merged.Get("hosts[1]"));
    }

    [Fact]
    public void A_scalar_replaces_a_mapping_when_the_later_layer_says_so()
    {
        var baseValues = Doc("resources:\n  limits:\n    cpu: 500m\n", "values.yaml");
        var overlay = Doc("resources: none\n", "values-dev.yaml");

        var merged = _merger.Merge([baseValues, overlay], "<merged>");

        Assert.Equal("none", merged.GetString("resources"));
    }

    [Fact]
    public void An_explicit_null_in_a_later_layer_clears_the_key()
    {
        var baseValues = Doc(
            """
            podDisruptionBudget:
              enabled: true
              minAvailable: 1
            nodeSelector:
              disk: ssd
            """,
            "values.yaml");

        var overlay = Doc(
            """
            nodeSelector: null
            podDisruptionBudget:
              minAvailable: ~
            """,
            "values-dev.yaml");

        var merged = _merger.Merge([baseValues, overlay], "<merged>");

        Assert.Null(merged.Get("nodeSelector"));
        Assert.Null(merged.Get("podDisruptionBudget.minAvailable"));
        Assert.True(merged.GetBool("podDisruptionBudget.enabled"));
    }

    [Fact]
    public void Three_layers_are_applied_in_order()
    {
        var first = Doc("a: 1\nb: 1\nc: 1\n", "values.yaml");
        var second = Doc("b: 2\nc: 2\n", "values-test.yaml");
        var third = Doc("c: 3\nd: 4\n", "values-prod.yaml");

        var merged = _merger.Merge([first, second, third], "<merged>");

        Assert.Equal(1, merged.GetInt("a"));
        Assert.Equal(2, merged.GetInt("b"));
        Assert.Equal(3, merged.GetInt("c"));
        Assert.Equal(4, merged.GetInt("d"));
    }

    [Fact]
    public void Deeply_nested_branches_merge_without_losing_siblings()
    {
        var baseValues = Doc(
            """
            istio:
              gateway:
                name: member-api-gateway
                host: member-api.example.com
              virtualService:
                timeout: 5s
                retries:
                  attempts: 2
            """,
            "values.yaml");

        var overlay = Doc(
            """
            istio:
              virtualService:
                retries:
                  attempts: 3
            """,
            "values-prod.yaml");

        var merged = _merger.Merge([baseValues, overlay], "<merged>");

        Assert.Equal("member-api-gateway", merged.GetString("istio.gateway.name"));
        Assert.Equal("5s", merged.GetString("istio.virtualService.timeout"));
        Assert.Equal(3, merged.GetInt("istio.virtualService.retries.attempts"));
    }

    [Fact]
    public void The_merged_yaml_round_trips()
    {
        var baseValues = Doc(
            """
            replicaCount: 2
            image:
              repository: ghcr.io/example/member-api
              tag: "1.12.0"
            hosts:
              - a.example.com
            """,
            "values.yaml");

        var overlay = Doc("replicaCount: 3\n", "values-prod.yaml");

        var merged = _merger.Merge([baseValues, overlay], "<merged>");
        var reparsed = ValuesDocument.Parse(merged.Yaml, "<reparsed>");

        Assert.Equal(3, reparsed.GetInt("replicaCount"));
        Assert.Equal("1.12.0", reparsed.GetString("image.tag"));
        Assert.Equal("a.example.com", reparsed.GetString("hosts[0]"));
    }

    [Fact]
    public void Merging_no_layers_yields_an_empty_document()
    {
        var merged = _merger.Merge([], "<merged>");

        Assert.Empty(merged.Root.Children);
        Assert.Empty(merged.Flatten());
    }

    [Fact]
    public void The_input_layers_are_never_mutated()
    {
        var baseValues = Doc("image:\n  tag: \"1.12.0\"\n", "values.yaml");
        var overlay = Doc("image:\n  tag: \"1.13.0\"\n", "values-prod.yaml");

        _merger.Merge([baseValues, overlay], "<merged>");

        Assert.Equal("1.12.0", baseValues.GetString("image.tag"));
        Assert.Equal("1.13.0", overlay.GetString("image.tag"));
    }
}
