namespace ChartPilot.Core.Manifests;

/// <summary>The grouping used by the Resource Explorer tree.</summary>
public enum ResourceCategory
{
    Workloads,
    Networking,
    Security,
    Certificates,
    Configuration,
    Scaling,
    Other
}

/// <summary>Maps a Kubernetes kind onto a <see cref="ResourceCategory"/>. Kinds are case-sensitive.</summary>
public static class ResourceCategorizer
{
    private static readonly IReadOnlyDictionary<string, ResourceCategory> Map =
        new Dictionary<string, ResourceCategory>(StringComparer.Ordinal)
        {
            // Workloads
            ["Deployment"] = ResourceCategory.Workloads,
            ["StatefulSet"] = ResourceCategory.Workloads,
            ["DaemonSet"] = ResourceCategory.Workloads,
            ["Job"] = ResourceCategory.Workloads,
            ["CronJob"] = ResourceCategory.Workloads,
            ["ReplicaSet"] = ResourceCategory.Workloads,
            ["Pod"] = ResourceCategory.Workloads,

            // Networking
            ["Service"] = ResourceCategory.Networking,
            ["Ingress"] = ResourceCategory.Networking,
            ["IngressClass"] = ResourceCategory.Networking,
            ["VirtualService"] = ResourceCategory.Networking,
            ["Gateway"] = ResourceCategory.Networking,
            ["DestinationRule"] = ResourceCategory.Networking,
            ["ServiceEntry"] = ResourceCategory.Networking,
            ["Sidecar"] = ResourceCategory.Networking,
            ["EndpointSlice"] = ResourceCategory.Networking,

            // Security
            ["ServiceAccount"] = ResourceCategory.Security,
            ["Role"] = ResourceCategory.Security,
            ["RoleBinding"] = ResourceCategory.Security,
            ["ClusterRole"] = ResourceCategory.Security,
            ["ClusterRoleBinding"] = ResourceCategory.Security,
            ["NetworkPolicy"] = ResourceCategory.Security,
            ["PodSecurityPolicy"] = ResourceCategory.Security,
            ["AuthorizationPolicy"] = ResourceCategory.Security,
            ["PeerAuthentication"] = ResourceCategory.Security,
            ["RequestAuthentication"] = ResourceCategory.Security,
            ["Secret"] = ResourceCategory.Security,

            // Certificates
            ["Certificate"] = ResourceCategory.Certificates,
            ["Issuer"] = ResourceCategory.Certificates,
            ["ClusterIssuer"] = ResourceCategory.Certificates,
            ["CertificateRequest"] = ResourceCategory.Certificates,

            // Configuration
            ["ConfigMap"] = ResourceCategory.Configuration,
            ["PersistentVolumeClaim"] = ResourceCategory.Configuration,
            ["PersistentVolume"] = ResourceCategory.Configuration,
            ["StorageClass"] = ResourceCategory.Configuration,
            ["CustomResourceDefinition"] = ResourceCategory.Configuration,

            // Scaling
            ["HorizontalPodAutoscaler"] = ResourceCategory.Scaling,
            ["VerticalPodAutoscaler"] = ResourceCategory.Scaling,
            ["PodDisruptionBudget"] = ResourceCategory.Scaling
        };

    public static ResourceCategory Categorize(string kind)
        => kind is not null && Map.TryGetValue(kind, out var category) ? category : ResourceCategory.Other;
}
