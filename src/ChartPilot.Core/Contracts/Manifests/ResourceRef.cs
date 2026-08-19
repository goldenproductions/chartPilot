namespace ChartPilot.Core.Manifests;

/// <summary>A reference to a rendered resource, used by findings and graph edges.</summary>
public sealed record ResourceRef(string Kind, string Name, string? Namespace = null)
{
    /// <summary>The display and lookup key, i.e. <c>Deployment/member-api</c>.</summary>
    public string Key => $"{Kind}/{Name}";

    public override string ToString() => Key;

    public static ResourceRef From(RenderedResource r) => new(r.Kind, r.Name, r.Namespace);
}
