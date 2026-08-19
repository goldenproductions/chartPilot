namespace ChartPilot.Core.Manifests;

/// <summary>
/// The rendered manifests, indexed by kind/name and enriched with resolved cross-references.
/// Checks query the graph instead of re-scanning YAML.
/// </summary>
public interface IResourceGraph
{
    IReadOnlyList<RenderedResource> Resources { get; }

    IReadOnlyList<GraphEdge> Edges { get; }

    IEnumerable<RenderedResource> ByKind(string kind);

    IEnumerable<RenderedResource> ByKinds(params string[] kinds);

    bool ContainsKind(string kind);

    RenderedResource? Find(string kind, string name);

    RenderedResource? Resolve(ResourceRef reference);

    /// <summary>Deployment, StatefulSet, DaemonSet, Job, CronJob, ReplicaSet, Pod — anything carrying a pod template.</summary>
    IEnumerable<RenderedResource> Workloads();

    /// <summary>Pod labels of a workload (spec.template.metadata.labels, or metadata.labels for a bare Pod).</summary>
    IReadOnlyDictionary<string, string> PodLabelsOf(RenderedResource workload);

    /// <summary>Resources of the given kind whose selector matches the supplied pod labels.</summary>
    IEnumerable<RenderedResource> SelectorMatches(string kind, IReadOnlyDictionary<string, string> podLabels);

    /// <summary>Workloads whose pod labels are matched by the supplied selector map (an empty selector matches all).</summary>
    IEnumerable<RenderedResource> WorkloadsMatchedBySelector(IReadOnlyDictionary<string, string> selector);

    IReadOnlyList<GraphEdge> EdgesFrom(ResourceRef source);

    IReadOnlyList<GraphEdge> EdgesTo(ResourceRef target);
}
