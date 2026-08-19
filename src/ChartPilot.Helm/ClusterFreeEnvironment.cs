namespace ChartPilot.Helm;

/// <summary>
/// The environment every helm invocation runs under. ChartPilot never contacts a cluster
/// (architecture.md section 6.2), and that is enforced by removing the variables helm and its plugins
/// would use to find one — for <em>every</em> launch, including <c>helm version</c>. One process
/// launch that skips the scrub is the pattern the next helm call gets copied from.
/// </summary>
internal static class ClusterFreeEnvironment
{
    /// <summary>Variables removed from the child process. A null value means "unset it".</summary>
    public static IReadOnlyDictionary<string, string?> Overrides { get; } =
        new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["KUBECONFIG"] = null,
            ["HELM_KUBECONTEXT"] = null,
            ["HELM_KUBEAPISERVER"] = null,
            ["HELM_KUBETOKEN"] = null
        };
}
