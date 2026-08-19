using ChartPilot.Core.Manifests;

namespace ChartPilot.Core.Tests.Manifests;

public sealed class ResourceGraphEdgeTests
{
    [Fact]
    public void VirtualService_routes_resolve_to_the_rendered_services()
    {
        var graph = ManifestFixtures.Graph("istio-resolved.yaml");

        var routes = ManifestFixtures.TargetsOf(graph, "VirtualService", "member-api", GraphRelations.RoutesTo);

        Assert.Equal(
            new[] { "Service/member-api", "Service/member-api-tcp", "Service/member-api-tls" },
            routes.ToArray());
        Assert.All(routes, key => Assert.NotNull(graph.Resolve(new ResourceRef("Service", key.Split('/')[1]))));
    }

    [Fact]
    public void VirtualService_gateways_resolve_and_the_mesh_gateway_is_ignored()
    {
        var graph = ManifestFixtures.Graph("istio-resolved.yaml");

        var gateways = ManifestFixtures.TargetsOf(graph, "VirtualService", "member-api", GraphRelations.BindsGateway);

        Assert.Equal(new[] { "Gateway/public-gateway" }, gateways.ToArray());
        Assert.DoesNotContain("Gateway/mesh", gateways);
    }

    [Fact]
    public void DestinationRule_applies_to_the_service_behind_its_host()
    {
        var graph = ManifestFixtures.Graph("istio-resolved.yaml");

        var applies = ManifestFixtures.TargetsOf(graph, "DestinationRule", "member-api", GraphRelations.AppliesTo);

        Assert.Equal(new[] { "Service/member-api" }, applies.ToArray());
        Assert.NotNull(graph.Resolve(new ResourceRef("Service", "member-api")));
    }

    [Fact]
    public void Istio_references_to_resources_the_chart_does_not_render_stay_unresolved()
    {
        var graph = ManifestFixtures.Graph("istio-dangling.yaml");

        var gateway = Assert.Single(
            graph.EdgesFrom(new ResourceRef("VirtualService", "member-api")),
            e => e.Relation == GraphRelations.BindsGateway);
        Assert.Equal("Gateway/shared-gateway", gateway.To.Key);
        Assert.Null(graph.Resolve(gateway.To));

        var route = Assert.Single(
            graph.EdgesFrom(new ResourceRef("VirtualService", "member-api")),
            e => e.Relation == GraphRelations.RoutesTo);
        Assert.Equal("Service/payments", route.To.Key);
        Assert.Null(graph.Resolve(route.To));

        var applies = Assert.Single(
            graph.EdgesFrom(new ResourceRef("DestinationRule", "payments")),
            e => e.Relation == GraphRelations.AppliesTo);
        Assert.Equal("Service/payments", applies.To.Key);
        Assert.Null(graph.Resolve(applies.To));
    }

    [Fact]
    public void Certificate_edges_resolve_to_the_issuer_and_the_tls_secret()
    {
        var graph = ManifestFixtures.Graph("certificate-resolved.yaml");

        var issuedBy = Assert.Single(ManifestFixtures.TargetsOf(graph, "Certificate", "member-api", GraphRelations.IssuedBy));
        Assert.Equal("ClusterIssuer/letsencrypt-prod", issuedBy);

        var writes = Assert.Single(ManifestFixtures.TargetsOf(graph, "Certificate", "member-api", GraphRelations.WritesSecret));
        Assert.Equal("Secret/member-api-tls", writes);

        Assert.All(
            graph.EdgesFrom(new ResourceRef("Certificate", "member-api")),
            e => Assert.NotNull(graph.Resolve(e.To)));
    }

    [Fact]
    public void Certificate_edges_exist_even_when_the_issuer_and_the_secret_are_missing()
    {
        var graph = ManifestFixtures.Graph("certificate-dangling.yaml");
        var edges = graph.EdgesFrom(new ResourceRef("Certificate", "member-api"));

        Assert.Equal(2, edges.Count);

        // An issuerRef without an explicit kind is a namespaced Issuer.
        var issuedBy = Assert.Single(edges, e => e.Relation == GraphRelations.IssuedBy);
        Assert.Equal("Issuer/platform-issuer", issuedBy.To.Key);
        Assert.Null(graph.Resolve(issuedBy.To));

        var writes = Assert.Single(edges, e => e.Relation == GraphRelations.WritesSecret);
        Assert.Equal("Secret/member-api-tls", writes.To.Key);
        Assert.Null(graph.Resolve(writes.To));
    }

    [Fact]
    public void Workload_edges_cover_service_accounts_env_and_volumes()
    {
        var graph = ManifestFixtures.Graph("workload-references.yaml");
        var deployment = new ResourceRef("Deployment", "member-api");

        Assert.Equal(
            new[] { "ServiceAccount/member-api" },
            ManifestFixtures.TargetsOf(graph, "Deployment", "member-api", GraphRelations.UsesServiceAccount).ToArray());

        Assert.Equal(
            new[] { "Secret/member-api-db", "Secret/member-api-external", "Secret/member-api-tls" },
            ManifestFixtures.TargetsOf(graph, "Deployment", "member-api", GraphRelations.MountsSecret).ToArray());

        Assert.Equal(
            new[] { "ConfigMap/member-api-config", "ConfigMap/platform-region" },
            ManifestFixtures.TargetsOf(graph, "Deployment", "member-api", GraphRelations.MountsConfigMap).ToArray());

        // The ConfigMap the chart renders resolves; the secrets it expects to exist already do not.
        Assert.NotNull(graph.Resolve(new ResourceRef("ConfigMap", "member-api-config")));
        Assert.Null(graph.Resolve(new ResourceRef("Secret", "member-api-db")));

        Assert.Equal(6, graph.EdgesFrom(deployment).Count);
    }

    [Fact]
    public void EdgesTo_finds_the_resources_pointing_at_a_target()
    {
        var graph = ManifestFixtures.Graph("workload-references.yaml");

        var incoming = graph.EdgesTo(new ResourceRef("ServiceAccount", "member-api"));

        var edge = Assert.Single(incoming);
        Assert.Equal("Deployment/member-api", edge.From.Key);
        Assert.Equal(GraphRelations.UsesServiceAccount, edge.Relation);
    }

    [Fact]
    public void Services_and_network_policies_select_workloads_by_pod_labels()
    {
        var graph = ManifestFixtures.Graph("selectors.yaml");

        Assert.Equal(
            new[] { "Deployment/member-api" },
            ManifestFixtures.TargetsOf(graph, "Service", "member-api", GraphRelations.Selects).ToArray());

        Assert.Equal(
            new[] { "Deployment/member-api" },
            ManifestFixtures.TargetsOf(graph, "NetworkPolicy", "member-api", GraphRelations.Selects).ToArray());

        // An empty podSelector is namespace-wide, so it selects every workload.
        Assert.Equal(
            new[] { "CronJob/member-api-reconcile", "Deployment/member-api" },
            ManifestFixtures.TargetsOf(graph, "NetworkPolicy", "default-deny", GraphRelations.Selects).ToArray());
    }

    [Fact]
    public void A_service_without_a_selector_selects_nothing()
    {
        var graph = ManifestFixtures.Graph("selectors.yaml");

        Assert.Empty(graph.EdgesFrom(new ResourceRef("Service", "member-api-external")));
    }

    [Fact]
    public void Istio_policies_cover_the_workloads_their_selector_matches()
    {
        var graph = ManifestFixtures.Graph("selectors.yaml");

        Assert.Equal(
            new[] { "Deployment/member-api" },
            ManifestFixtures.TargetsOf(graph, "AuthorizationPolicy", "member-api", GraphRelations.Covers).ToArray());

        // No selector means namespace-wide.
        Assert.Equal(
            new[] { "CronJob/member-api-reconcile", "Deployment/member-api" },
            ManifestFixtures.TargetsOf(graph, "PeerAuthentication", "default", GraphRelations.Covers).ToArray());
    }

    [Fact]
    public void Pdb_and_hpa_target_their_workload()
    {
        var graph = ManifestFixtures.Graph("selectors.yaml");

        Assert.Equal(
            new[] { "Deployment/member-api" },
            ManifestFixtures.TargetsOf(graph, "PodDisruptionBudget", "member-api", GraphRelations.TargetsWorkload).ToArray());

        Assert.Equal(
            new[] { "Deployment/member-api" },
            ManifestFixtures.TargetsOf(graph, "HorizontalPodAutoscaler", "member-api", GraphRelations.TargetsWorkload).ToArray());
    }

    [Fact]
    public void An_hpa_pointing_at_a_workload_the_chart_does_not_render_stays_unresolved()
    {
        var graph = ManifestFixtures.GraphOfText(
            string.Join("\n",
                "apiVersion: autoscaling/v2",
                "kind: HorizontalPodAutoscaler",
                "metadata:",
                "  name: member-api",
                "spec:",
                "  scaleTargetRef:",
                "    apiVersion: apps/v1",
                "    kind: Deployment",
                "    name: legacy-importer",
                "  maxReplicas: 5"));

        var edge = Assert.Single(graph.EdgesFrom(new ResourceRef("HorizontalPodAutoscaler", "member-api")));

        Assert.Equal("Deployment/legacy-importer", edge.To.Key);
        Assert.Equal(GraphRelations.TargetsWorkload, edge.Relation);
        Assert.Null(graph.Resolve(edge.To));
    }

    [Fact]
    public void Edges_are_deduplicated_and_deterministically_ordered()
    {
        var graph = ManifestFixtures.Graph("selectors.yaml");
        var keys = graph.Edges.Select(e => (e.From.Key, e.Relation, e.To.Key)).ToArray();

        Assert.Equal(keys.Distinct().Count(), keys.Length);

        var sorted = keys
            .OrderBy(k => k.Item1, StringComparer.Ordinal)
            .ThenBy(k => k.Item2, StringComparer.Ordinal)
            .ThenBy(k => k.Item3, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(sorted, keys);
    }

    [Fact]
    public void Duplicate_references_produce_a_single_edge()
    {
        var graph = ManifestFixtures.GraphOfText(
            string.Join("\n",
                "apiVersion: apps/v1",
                "kind: Deployment",
                "metadata:",
                "  name: member-api",
                "spec:",
                "  template:",
                "    metadata:",
                "      labels:",
                "        app: member-api",
                "    spec:",
                "      volumes:",
                "        - name: a",
                "          secret:",
                "            secretName: shared",
                "        - name: b",
                "          secret:",
                "            secretName: shared",
                "      containers:",
                "        - name: app",
                "          image: ghcr.io/acme/member-api:1.4.2",
                "          envFrom:",
                "            - secretRef:",
                "                name: shared"));

        var edges = graph.EdgesFrom(new ResourceRef("Deployment", "member-api"));

        var edge = Assert.Single(edges);
        Assert.Equal("Secret/shared", edge.To.Key);
        Assert.Equal(GraphRelations.MountsSecret, edge.Relation);
    }
}
