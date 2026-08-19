namespace ChartPilot.Core.Manifests;

/// <summary>A resolved cross-reference between two rendered resources.</summary>
public sealed record GraphEdge(ResourceRef From, ResourceRef To, string Relation);

/// <summary>The relation names used by <see cref="GraphEdge.Relation"/>.</summary>
public static class GraphRelations
{
    /// <summary>VirtualService -&gt; Service.</summary>
    public const string RoutesTo = "routes-to";

    /// <summary>VirtualService -&gt; Gateway.</summary>
    public const string BindsGateway = "binds-gateway";

    /// <summary>Certificate -&gt; Issuer/ClusterIssuer.</summary>
    public const string IssuedBy = "issued-by";

    /// <summary>Certificate -&gt; the TLS Secret it writes.</summary>
    public const string WritesSecret = "writes-secret";

    /// <summary>Workload -&gt; ServiceAccount.</summary>
    public const string UsesServiceAccount = "uses-service-account";

    /// <summary>Service/NetworkPolicy -&gt; workload.</summary>
    public const string Selects = "selects";

    /// <summary>AuthorizationPolicy/PeerAuthentication -&gt; workload.</summary>
    public const string Covers = "covers";

    /// <summary>DestinationRule -&gt; Service.</summary>
    public const string AppliesTo = "applies-to";

    /// <summary>Workload -&gt; Secret.</summary>
    public const string MountsSecret = "mounts-secret";

    /// <summary>Workload -&gt; ConfigMap.</summary>
    public const string MountsConfigMap = "mounts-config-map";

    /// <summary>PodDisruptionBudget/HorizontalPodAutoscaler -&gt; workload.</summary>
    public const string TargetsWorkload = "targets-workload";
}
