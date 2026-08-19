using ChartPilot.Core.Manifests;

namespace ChartPilot.Core.Tests.Manifests;

public sealed class ManifestParserTests
{
    [Fact]
    public void Parses_every_document_of_a_normal_stream()
    {
        var resources = ManifestFixtures.Parse("multi-doc.yaml");

        Assert.Equal(4, resources.Count);
        Assert.Equal(
            new[] { "ServiceAccount/member-api", "ConfigMap/member-api-config", "Service/member-api", "Deployment/member-api" },
            resources.Select(r => r.Ref.Key).ToArray());
    }

    [Fact]
    public void Captures_apiVersion_kind_and_source_template()
    {
        var deployment = ManifestFixtures.Parse("multi-doc.yaml").Single(r => r.Kind == "Deployment");

        Assert.Equal("apps/v1", deployment.ApiVersion);
        Assert.Equal("apps", deployment.ApiGroup);
        Assert.Equal("member-api", deployment.Name);
        Assert.Equal("member-api/templates/deployment.yaml", deployment.SourceTemplate);
    }

    [Fact]
    public void Namespace_is_null_when_absent_and_set_when_present()
    {
        var resources = ManifestFixtures.Parse("multi-doc.yaml");

        Assert.Null(resources.Single(r => r.Kind == "Deployment").Namespace);
        Assert.Equal("member-prod", resources.Single(r => r.Kind == "ConfigMap").Namespace);
    }

    [Fact]
    public void Raw_yaml_excludes_the_separator_and_the_source_comment()
    {
        var serviceAccount = ManifestFixtures.Parse("multi-doc.yaml").First();

        var expected = string.Join("\n",
            "apiVersion: v1",
            "kind: ServiceAccount",
            "metadata:",
            "  name: member-api",
            "  labels:",
            "    app: member-api");

        Assert.Equal(expected, serviceAccount.Yaml);
    }

    [Fact]
    public void Raw_yaml_of_the_last_document_is_trimmed()
    {
        var deployment = ManifestFixtures.Parse("multi-doc.yaml").Last();

        Assert.StartsWith("apiVersion: apps/v1", deployment.Yaml, StringComparison.Ordinal);
        Assert.EndsWith("image: ghcr.io/acme/member-api:1.4.2", deployment.Yaml, StringComparison.Ordinal);
        Assert.DoesNotContain("---", deployment.Yaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Empty_and_comment_only_documents_are_skipped()
    {
        var resources = ManifestFixtures.Parse("empty-and-comment-documents.yaml");

        Assert.Equal(2, resources.Count);
        Assert.Equal(new[] { "first", "second" }, resources.Select(r => r.Name).ToArray());
        Assert.Equal("member-api/templates/second.yaml", resources[1].SourceTemplate);
        Assert.DoesNotContain("comment-only", resources[0].Yaml, StringComparison.Ordinal);
    }

    [Fact]
    public void List_wrapper_is_expanded_into_its_items()
    {
        var resources = ManifestFixtures.Parse("list-wrapper.yaml");

        Assert.Equal(2, resources.Count);
        Assert.Equal(new[] { "ConfigMap/list-one", "Secret/list-two" }, resources.Select(r => r.Ref.Key).ToArray());
        Assert.All(resources, r => Assert.Equal("member-api/templates/list.yaml", r.SourceTemplate));
        Assert.Equal("member-prod", resources[1].Namespace);

        // Items are re-emitted at column zero so they display like ordinary documents.
        Assert.StartsWith("apiVersion: v1", resources[0].Yaml, StringComparison.Ordinal);
        Assert.Contains("name: list-one", resources[0].Yaml, StringComparison.Ordinal);
        Assert.DoesNotContain("kind: List", resources[0].Yaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Crds_are_parsed_as_ordinary_resources()
    {
        var resources = ManifestFixtures.Parse("crds.yaml");

        var crd = resources.Single(r => r.Kind == "CustomResourceDefinition");
        Assert.Equal("widgets.acme.io", crd.Name);
        Assert.Equal("apiextensions.k8s.io", crd.ApiGroup);
        Assert.Equal("member-api/crds/widgets.yaml", crd.SourceTemplate);
        Assert.Equal(ResourceCategory.Configuration, ResourceCategorizer.Categorize(crd.Kind));
    }

    [Fact]
    public void A_separator_inside_a_block_scalar_does_not_split_the_document()
    {
        var resources = ManifestFixtures.Parse("block-scalar-separator.yaml");

        Assert.Equal(2, resources.Count);

        var configMap = resources[0];
        Assert.Equal("scripts", configMap.Name);
        Assert.Contains("still the same document", configMap.Yaml, StringComparison.Ordinal);
        Assert.Contains("    ---", configMap.Yaml, StringComparison.Ordinal);
        Assert.Contains("a --- inside a quoted string", configMap.Yaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Documents_without_a_kind_or_a_name_are_skipped()
    {
        var resources = ManifestFixtures.Parse("skipped-documents.yaml");

        Assert.Single(resources);
        Assert.Equal("ConfigMap/good", resources[0].Ref.Key);
    }

    [Fact]
    public void Empty_input_yields_no_resources()
    {
        Assert.Empty(ManifestFixtures.ParseText(string.Empty));
        Assert.Empty(ManifestFixtures.ParseText("   \n\n"));
        Assert.Empty(ManifestFixtures.ParseText("# nothing rendered\n"));
    }

    [Fact]
    public void Malformed_yaml_throws_with_the_document_index_and_the_parser_message()
    {
        var yaml = ManifestFixtures.Read("malformed.yaml");

        var ex = Assert.Throws<ManifestParseException>(() => new ManifestParser().Parse(yaml));

        Assert.Equal(1, ex.DocumentIndex);
        Assert.NotNull(ex.Line);
        Assert.NotNull(ex.InnerException);
        Assert.Contains("Failed to parse rendered manifest document 1", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Windows_line_endings_are_normalized()
    {
        var yaml = "---\r\n# Source: c/templates/a.yaml\r\napiVersion: v1\r\nkind: ConfigMap\r\nmetadata:\r\n  name: a\r\n";

        var resource = Assert.Single(ManifestFixtures.ParseText(yaml));

        Assert.Equal("c/templates/a.yaml", resource.SourceTemplate);
        Assert.False(resource.Yaml.Contains('\r'));
        Assert.Equal("apiVersion: v1\nkind: ConfigMap\nmetadata:\n  name: a", resource.Yaml);
    }

    [Fact]
    public void A_missing_source_comment_yields_an_empty_source_template()
    {
        var yaml = "apiVersion: v1\nkind: ConfigMap\nmetadata:\n  name: a\n";

        var resource = Assert.Single(ManifestFixtures.ParseText(yaml));

        Assert.Equal(string.Empty, resource.SourceTemplate);
    }

    [Fact]
    public void An_explicitly_empty_namespace_reads_as_null()
    {
        var yaml = "apiVersion: v1\nkind: ConfigMap\nmetadata:\n  name: a\n  namespace: \"\"\n";

        var resource = Assert.Single(ManifestFixtures.ParseText(yaml));

        Assert.Null(resource.Namespace);
    }
}
