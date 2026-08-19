using ChartPilot.Core.Values;

namespace ChartPilot.Core.Tests.Values;

public class ValuesValidatorTests
{
    private const string Schema = """
        {
          "$schema": "http://json-schema.org/draft-07/schema#",
          "type": "object",
          "required": ["replicaCount", "image"],
          "properties": {
            "replicaCount": { "type": "integer", "minimum": 1 },
            "image": {
              "type": "object",
              "required": ["repository", "tag"],
              "properties": {
                "repository": { "type": "string" },
                "tag": { "type": "string" }
              }
            },
            "hosts": {
              "type": "array",
              "items": { "type": "string" }
            },
            "enabled": { "type": "boolean" }
          }
        }
        """;

    private const string ValidValues = """
        replicaCount: 2
        image:
          repository: ghcr.io/example/member-api
          tag: "1.12.0"
        hosts:
          - member-api.example.com
        enabled: true
        """;

    private readonly ValuesValidator _validator = new();

    [Fact]
    public void Well_formed_yaml_is_valid()
    {
        var result = _validator.ValidateYaml(ValidValues);

        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void Empty_yaml_is_valid()
    {
        var result = _validator.ValidateYaml("   \n# only a comment\n");

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Malformed_yaml_yields_one_issue_carrying_the_position()
    {
        const string broken = """
            replicaCount: 2
              image: wrongly-indented
            """;

        var result = _validator.ValidateYaml(broken);

        Assert.False(result.IsValid);
        var issue = Assert.Single(result.Issues);
        Assert.Equal(string.Empty, issue.Path);
        Assert.Equal("yaml", issue.Keyword);
        Assert.Contains("line", issue.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_values_document_that_is_not_a_mapping_is_invalid()
    {
        var result = _validator.ValidateYaml("- one\n- two\n");

        Assert.False(result.IsValid);
        Assert.Single(result.Issues);
    }

    [Fact]
    public void Values_matching_the_schema_are_valid()
    {
        var result = _validator.ValidateAgainstSchema(ValidValues, Schema);

        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void A_violated_constraint_is_reported_against_its_dotted_path()
    {
        const string values = """
            replicaCount: 0
            image:
              repository: ghcr.io/example/member-api
              tag: "1.12.0"
            """;

        var result = _validator.ValidateAgainstSchema(values, Schema);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, i => i.Path == "replicaCount");
        Assert.Contains(result.Issues, i => i.Keyword is not null);
    }

    [Fact]
    public void A_nested_type_mismatch_is_reported_against_the_nested_path()
    {
        const string values = """
            replicaCount: 2
            image:
              repository: ghcr.io/example/member-api
              tag: 5
            """;

        var result = _validator.ValidateAgainstSchema(values, Schema);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, i => i.Path == "image.tag");
    }

    [Fact]
    public void A_sequence_element_is_reported_with_an_indexer()
    {
        const string values = """
            replicaCount: 2
            image:
              repository: ghcr.io/example/member-api
              tag: "1.12.0"
            hosts:
              - 42
            """;

        var result = _validator.ValidateAgainstSchema(values, Schema);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, i => i.Path == "hosts[0]");
    }

    [Fact]
    public void A_missing_required_property_is_reported_at_the_root()
    {
        const string values = """
            replicaCount: 2
            """;

        var result = _validator.ValidateAgainstSchema(values, Schema);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, i => i.Path == string.Empty);
    }

    [Fact]
    public void A_quoted_number_stays_a_string_for_schema_purposes()
    {
        const string values = """
            replicaCount: 2
            image:
              repository: ghcr.io/example/member-api
              tag: "1"
            """;

        Assert.True(_validator.ValidateAgainstSchema(values, Schema).IsValid);
    }

    [Fact]
    public void A_plain_yes_is_treated_as_a_boolean()
    {
        const string values = """
            replicaCount: 2
            image:
              repository: ghcr.io/example/member-api
              tag: "1.12.0"
            enabled: yes
            """;

        Assert.True(_validator.ValidateAgainstSchema(values, Schema).IsValid);
    }

    [Fact]
    public void An_unparseable_schema_is_reported_rather_than_thrown()
    {
        var result = _validator.ValidateAgainstSchema(ValidValues, "{ this is not json");

        Assert.False(result.IsValid);
        var issue = Assert.Single(result.Issues);
        Assert.Equal("schema", issue.Keyword);
    }

    [Fact]
    public void Malformed_yaml_short_circuits_schema_validation()
    {
        var result = _validator.ValidateAgainstSchema("replicaCount: 2\n  image: bad\n", Schema);

        Assert.False(result.IsValid);
        var issue = Assert.Single(result.Issues);
        Assert.Equal("yaml", issue.Keyword);
    }
}
