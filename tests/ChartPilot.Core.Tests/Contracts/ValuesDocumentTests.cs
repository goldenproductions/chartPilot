using ChartPilot.Core.Values;

namespace ChartPilot.Core.Tests.Contracts;

public class ValuesDocumentTests
{
    private const string Yaml = """
        replicaCount: 3
        image:
          repository: ghcr.io/example/member-api
          tag: "1.4.2"
          pullPolicy: IfNotPresent
        ingress:
          enabled: true
          hosts:
            - host: api.example.com
              paths:
                - /
        platform:
          dataClassification: sensitive-personal-data
          exposure: internal
        """;

    [Fact]
    public void Parse_reads_scalars_by_dotted_path()
    {
        var document = ValuesDocument.Parse(Yaml, "values.yaml");

        Assert.Equal("values.yaml", document.SourceName);
        Assert.Equal(3, document.GetInt("replicaCount"));
        Assert.Equal("1.4.2", document.GetString("image.tag"));
        Assert.True(document.GetBool("ingress.enabled"));
        Assert.Equal("api.example.com", document.GetString("ingress.hosts[0].host"));
        Assert.Null(document.GetString("image.missing"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n\t  ")]
    [InlineData("# only a comment\n# and another\n")]
    public void Parse_of_an_effectively_empty_document_yields_an_empty_mapping(string yaml)
    {
        var document = ValuesDocument.Parse(yaml, "values.yaml");

        Assert.Empty(document.Root.Children);
        Assert.Empty(document.Flatten());
    }

    [Fact]
    public void Empty_creates_a_usable_document()
    {
        var document = ValuesDocument.Empty("<draft>");

        Assert.Equal("<draft>", document.SourceName);
        Assert.Equal(string.Empty, document.Yaml);
        Assert.Empty(document.Root.Children);
    }

    [Fact]
    public void Parse_of_malformed_yaml_throws_with_a_position()
    {
        const string malformed = """
            image:
              repository: ok
             tag: bad-indent
            """;

        var exception = Assert.Throws<ValuesParseException>(() => ValuesDocument.Parse(malformed, "values.yaml"));

        Assert.NotNull(exception.Line);
        Assert.NotNull(exception.Column);
    }

    [Fact]
    public void Parse_rejects_a_non_mapping_root()
    {
        Assert.Throws<ValuesParseException>(() => ValuesDocument.Parse("- one\n- two\n", "values.yaml"));
    }

    [Fact]
    public void Flatten_produces_dotted_paths_with_sequence_indexers()
    {
        var flat = ValuesDocument.Parse(Yaml, "values.yaml").Flatten();

        Assert.Equal("3", flat["replicaCount"]);
        Assert.Equal("1.4.2", flat["image.tag"]);
        Assert.Equal("api.example.com", flat["ingress.hosts[0].host"]);
        Assert.Equal("/", flat["ingress.hosts[0].paths[0]"]);
        Assert.Equal("sensitive-personal-data", flat["platform.dataClassification"]);
        Assert.False(flat.ContainsKey("image"));
    }

    [Fact]
    public void Flatten_keeps_null_leaves_as_present_with_a_null_value()
    {
        var flat = ValuesDocument.Parse("resources:\n  limits:\n    cpu:\n", "values.yaml").Flatten();

        Assert.True(flat.ContainsKey("resources.limits.cpu"));
        Assert.Null(flat["resources.limits.cpu"]);
    }

    [Fact]
    public void Flatten_is_cached()
    {
        var document = ValuesDocument.Parse(Yaml, "values.yaml");

        Assert.Same(document.Flatten(), document.Flatten());
    }
}
