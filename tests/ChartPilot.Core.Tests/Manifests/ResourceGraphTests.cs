using ChartPilot.Core.Manifests;

namespace ChartPilot.Core.Tests.Manifests;

public sealed class ResourceGraphTests
{
    private static readonly Dictionary<string, string> Empty = new(StringComparer.Ordinal);

    [Fact]
    public void Indexes_resources_by_kind_and_name()
    {
        var graph = ManifestFixtures.Graph("multi-doc.yaml");

        Assert.Equal(4, graph.Resources.Count);
        Assert.NotNull(graph.Find("Deployment", "member-api"));
        Assert.NotNull(graph.Find("Service", "member-api"));
        Assert.Null(graph.Find("Deployment", "unknown"));

        // The kind half of the key is case-sensitive.
        Assert.Null(graph.Find("deployment", "member-api"));
    }

    [Fact]
    public void Resolve_returns_the_resource_behind_a_reference()
    {
        var graph = ManifestFixtures.Graph("multi-doc.yaml");

        Assert.Equal("member-api", graph.Resolve(new ResourceRef("ServiceAccount", "member-api"))?.Name);
        Assert.Null(graph.Resolve(new ResourceRef("ServiceAccount", "other")));
    }

    [Fact]
    public void ByKind_and_ByKinds_filter_the_render()
    {
        var graph = ManifestFixtures.Graph("selectors.yaml");

        Assert.Equal(2, graph.ByKind("Service").Count());
        Assert.Empty(graph.ByKind("Ingress"));
        Assert.True(graph.ContainsKind("NetworkPolicy"));
        Assert.False(graph.ContainsKind("Ingress"));

        var mixed = graph.ByKinds("Service", "NetworkPolicy").Select(r => r.Ref.Key).ToArray();
        Assert.Equal(4, mixed.Length);
        Assert.Contains("Service/member-api", mixed);
        Assert.Contains("NetworkPolicy/default-deny", mixed);
        Assert.Empty(graph.ByKinds());
    }

    [Fact]
    public void Workloads_are_the_kinds_that_carry_a_pod_template()
    {
        var graph = ManifestFixtures.Graph("selectors.yaml");

        Assert.Equal(
            new[] { "Deployment/member-api", "CronJob/member-api-reconcile" },
            graph.Workloads().Select(r => r.Ref.Key).ToArray());
    }

    [Fact]
    public void PodLabelsOf_reads_the_pod_template_of_each_workload_shape()
    {
        var graph = ManifestFixtures.Graph("selectors.yaml");

        var deployment = graph.PodLabelsOf(graph.Find("Deployment", "member-api")!);
        Assert.Equal("member-api", deployment["app"]);
        Assert.Equal("backend", deployment["tier"]);

        var cronJob = graph.PodLabelsOf(graph.Find("CronJob", "member-api-reconcile")!);
        Assert.Equal("member-api-reconcile", cronJob["app"]);
    }

    [Fact]
    public void PodLabelsOf_reads_metadata_labels_for_a_bare_pod()
    {
        var graph = ManifestFixtures.GraphOfText(
            string.Join("\n",
                "apiVersion: v1",
                "kind: Pod",
                "metadata:",
                "  name: debug",
                "  labels:",
                "    app: debug",
                "spec:",
                "  containers:",
                "    - name: shell",
                "      image: busybox:1.36"));

        var pod = graph.Find("Pod", "debug")!;

        Assert.Equal("debug", graph.PodLabelsOf(pod)["app"]);
    }

    [Fact]
    public void SelectorMatches_handles_the_flat_and_the_matchLabels_selector_forms()
    {
        var graph = ManifestFixtures.Graph("selectors.yaml");
        var podLabels = new Dictionary<string, string>(StringComparer.Ordinal) { ["app"] = "member-api" };

        // Service uses the flat spec.selector map.
        Assert.Contains(graph.SelectorMatches("Service", podLabels), r => r.Name == "member-api");

        // NetworkPolicy uses spec.podSelector.matchLabels.
        Assert.Contains(graph.SelectorMatches("NetworkPolicy", podLabels), r => r.Name == "member-api");

        // PodDisruptionBudget uses spec.selector.matchLabels.
        Assert.Contains(graph.SelectorMatches("PodDisruptionBudget", podLabels), r => r.Name == "member-api");
    }

    [Fact]
    public void SelectorMatches_accepts_pod_labels_that_are_a_superset_of_the_selector()
    {
        var graph = ManifestFixtures.Graph("selectors.yaml");
        var podLabels = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["app"] = "member-api",
            ["tier"] = "backend",
            ["app.kubernetes.io/managed-by"] = "Helm"
        };

        Assert.Contains(graph.SelectorMatches("Service", podLabels), r => r.Name == "member-api");
    }

    [Fact]
    public void SelectorMatches_rejects_pod_labels_that_miss_a_selector_key()
    {
        var graph = ManifestFixtures.Graph("selectors.yaml");
        var podLabels = new Dictionary<string, string>(StringComparer.Ordinal) { ["tier"] = "backend" };

        var matched = graph.SelectorMatches("NetworkPolicy", podLabels).Select(r => r.Name).ToArray();

        // Only the default-deny policy, whose podSelector is empty, still matches.
        Assert.Equal(new[] { "default-deny" }, matched);
    }

    [Fact]
    public void An_empty_or_absent_selector_matches_every_pod()
    {
        var graph = ManifestFixtures.Graph("selectors.yaml");

        // ExternalName Service: no selector at all.
        Assert.Contains(graph.SelectorMatches("Service", Empty), r => r.Name == "member-api-external");

        // Empty podSelector: the namespace-wide default-deny policy.
        Assert.Contains(graph.SelectorMatches("NetworkPolicy", Empty), r => r.Name == "default-deny");

        // A selector with keys does not match a pod with no labels.
        Assert.DoesNotContain(graph.SelectorMatches("NetworkPolicy", Empty), r => r.Name == "member-api");
    }

    [Fact]
    public void WorkloadsMatchedBySelector_is_the_inverse_of_SelectorMatches()
    {
        var graph = ManifestFixtures.Graph("selectors.yaml");

        var matched = graph
            .WorkloadsMatchedBySelector(new Dictionary<string, string>(StringComparer.Ordinal) { ["app"] = "member-api" })
            .Select(r => r.Ref.Key)
            .ToArray();

        Assert.Equal(new[] { "Deployment/member-api" }, matched);

        var all = graph.WorkloadsMatchedBySelector(Empty).Select(r => r.Ref.Key).ToArray();
        Assert.Equal(new[] { "Deployment/member-api", "CronJob/member-api-reconcile" }, all);
    }

    [Fact]
    public void An_empty_render_produces_an_empty_graph()
    {
        var graph = new ResourceGraphBuilder().Build(Array.Empty<RenderedResource>());

        Assert.Empty(graph.Resources);
        Assert.Empty(graph.Edges);
        Assert.Empty(graph.Workloads());
        Assert.Null(graph.Find("Deployment", "member-api"));
        Assert.Empty(graph.EdgesFrom(new ResourceRef("Deployment", "member-api")));
        Assert.Empty(graph.EdgesTo(new ResourceRef("Deployment", "member-api")));
    }
}
