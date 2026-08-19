using YamlDotNet.RepresentationModel;

namespace ChartPilot.Core.Manifests;

/// <summary>
/// The default <see cref="IResourceGraph"/>: the rendered resources indexed by <c>kind/name</c> and by kind,
/// plus the pre-resolved cross-reference edges. Instances are immutable and are produced by
/// <see cref="ResourceGraphBuilder"/>.
/// </summary>
internal sealed class ResourceGraph : IResourceGraph
{
    private static readonly IReadOnlyDictionary<string, string> EmptyMap =
        new Dictionary<string, string>(StringComparer.Ordinal);

    private static readonly IReadOnlyList<GraphEdge> NoEdges = Array.Empty<GraphEdge>();

    /// <summary>Kinds that carry a pod template and are therefore treated as workloads.</summary>
    internal static readonly IReadOnlySet<string> WorkloadKinds = new HashSet<string>(StringComparer.Ordinal)
    {
        "Deployment", "StatefulSet", "DaemonSet", "Job", "CronJob", "ReplicaSet", "Pod"
    };

    private readonly Dictionary<string, RenderedResource> _byKey;
    private readonly Dictionary<string, List<RenderedResource>> _byKind;
    private readonly Dictionary<string, List<GraphEdge>> _edgesFrom;
    private readonly Dictionary<string, List<GraphEdge>> _edgesTo;

    internal ResourceGraph(IReadOnlyList<RenderedResource> resources, IReadOnlyList<GraphEdge> edges)
    {
        Resources = resources ?? Array.Empty<RenderedResource>();
        Edges = edges ?? Array.Empty<GraphEdge>();

        _byKey = new Dictionary<string, RenderedResource>(StringComparer.Ordinal);
        _byKind = new Dictionary<string, List<RenderedResource>>(StringComparer.Ordinal);

        foreach (var resource in Resources)
        {
            // First document wins: a chart that renders the same kind/name twice is a chart problem,
            // and the graph stays deterministic either way.
            _byKey.TryAdd(resource.Ref.Key, resource);

            if (!_byKind.TryGetValue(resource.Kind, out var bucket))
            {
                bucket = new List<RenderedResource>();
                _byKind[resource.Kind] = bucket;
            }

            bucket.Add(resource);
        }

        _edgesFrom = new Dictionary<string, List<GraphEdge>>(StringComparer.Ordinal);
        _edgesTo = new Dictionary<string, List<GraphEdge>>(StringComparer.Ordinal);

        foreach (var edge in Edges)
        {
            Add(_edgesFrom, edge.From.Key, edge);
            Add(_edgesTo, edge.To.Key, edge);
        }

        static void Add(Dictionary<string, List<GraphEdge>> index, string key, GraphEdge edge)
        {
            if (!index.TryGetValue(key, out var bucket))
            {
                bucket = new List<GraphEdge>();
                index[key] = bucket;
            }

            bucket.Add(edge);
        }
    }

    public IReadOnlyList<RenderedResource> Resources { get; }

    public IReadOnlyList<GraphEdge> Edges { get; }

    public IEnumerable<RenderedResource> ByKind(string kind)
        => kind is not null && _byKind.TryGetValue(kind, out var bucket) ? bucket : Array.Empty<RenderedResource>();

    public IEnumerable<RenderedResource> ByKinds(params string[] kinds)
    {
        if (kinds is null || kinds.Length == 0)
        {
            return Array.Empty<RenderedResource>();
        }

        var wanted = new HashSet<string>(kinds, StringComparer.Ordinal);
        return Resources.Where(r => wanted.Contains(r.Kind));
    }

    public bool ContainsKind(string kind) => kind is not null && _byKind.ContainsKey(kind);

    public RenderedResource? Find(string kind, string name)
    {
        if (kind is null || name is null)
        {
            return null;
        }

        return _byKey.TryGetValue($"{kind}/{name}", out var resource) ? resource : null;
    }

    public RenderedResource? Resolve(ResourceRef reference)
        => reference is null ? null : Find(reference.Kind, reference.Name);

    public IEnumerable<RenderedResource> Workloads()
        => Resources.Where(r => WorkloadKinds.Contains(r.Kind));

    public IReadOnlyDictionary<string, string> PodLabelsOf(RenderedResource workload)
    {
        if (workload is null)
        {
            return EmptyMap;
        }

        return workload.Kind switch
        {
            "Pod" => ManifestNavigator.GetStringMap(workload.Root, "metadata.labels"),
            "CronJob" => ManifestNavigator.GetStringMap(workload.Root, "spec.jobTemplate.spec.template.metadata.labels"),
            _ => ManifestNavigator.GetStringMap(workload.Root, "spec.template.metadata.labels")
        };
    }

    public IEnumerable<RenderedResource> SelectorMatches(string kind, IReadOnlyDictionary<string, string> podLabels)
    {
        var labels = podLabels ?? EmptyMap;

        foreach (var resource in ByKind(kind))
        {
            if (SelectorReader.Matches(SelectorReader.SelectorOf(resource), labels))
            {
                yield return resource;
            }
        }
    }

    public IEnumerable<RenderedResource> WorkloadsMatchedBySelector(IReadOnlyDictionary<string, string> selector)
    {
        var wanted = selector ?? EmptyMap;

        foreach (var workload in Workloads())
        {
            if (SelectorReader.Matches(wanted, PodLabelsOf(workload)))
            {
                yield return workload;
            }
        }
    }

    public IReadOnlyList<GraphEdge> EdgesFrom(ResourceRef source)
        => source is not null && _edgesFrom.TryGetValue(source.Key, out var bucket) ? bucket : NoEdges;

    public IReadOnlyList<GraphEdge> EdgesTo(ResourceRef target)
        => target is not null && _edgesTo.TryGetValue(target.Key, out var bucket) ? bucket : NoEdges;
}

/// <summary>
/// Reads Kubernetes and Istio selectors out of a rendered resource and matches them against pod labels.
/// Both the flat <c>spec.selector</c> map (Service) and the <c>matchLabels</c> forms
/// (NetworkPolicy, PodDisruptionBudget, AuthorizationPolicy, Sidecar) are supported.
/// </summary>
internal static class SelectorReader
{
    private static readonly IReadOnlyDictionary<string, string> Empty =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>The selector of a resource, or an empty map when it has none.</summary>
    internal static IReadOnlyDictionary<string, string> SelectorOf(RenderedResource resource)
    {
        if (resource is null)
        {
            return Empty;
        }

        // NetworkPolicy puts the pod selector under a different key than everything else.
        var podSelector = ManifestNavigator.GetStringMap(resource.Root, "spec.podSelector.matchLabels");
        if (podSelector.Count > 0)
        {
            return podSelector;
        }

        var matchLabels = ManifestNavigator.GetStringMap(resource.Root, "spec.selector.matchLabels");
        if (matchLabels.Count > 0)
        {
            return matchLabels;
        }

        // Service and friends: spec.selector is the label map itself.
        if (ManifestNavigator.Get(resource.Root, "spec.selector") is YamlMappingNode)
        {
            var flat = ManifestNavigator.GetStringMap(resource.Root, "spec.selector");
            if (flat.Count > 0)
            {
                return flat;
            }
        }

        return Empty;
    }

    /// <summary>True when the resource declares no selector keys at all.</summary>
    internal static bool HasNoSelector(RenderedResource resource) => SelectorOf(resource).Count == 0;

    /// <summary>
    /// A selector matches when every one of its key/value pairs is present in the labels.
    /// An empty selector matches everything.
    /// </summary>
    internal static bool Matches(IReadOnlyDictionary<string, string> selector, IReadOnlyDictionary<string, string> labels)
    {
        if (selector is null || selector.Count == 0)
        {
            return true;
        }

        if (labels is null || labels.Count == 0)
        {
            return false;
        }

        foreach (var entry in selector)
        {
            if (!labels.TryGetValue(entry.Key, out var value) || !string.Equals(value, entry.Value, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }
}
