using YamlDotNet.RepresentationModel;

namespace ChartPilot.Core.Manifests;

/// <summary>
/// A single Kubernetes document produced by <c>helm template</c>.
/// </summary>
public sealed record RenderedResource(
    string ApiVersion,
    string Kind,
    string Name,
    string? Namespace,
    string SourceTemplate,
    YamlNode Root,
    string Yaml)
{
    /// <summary>Stable identity of this resource inside a render.</summary>
    public ResourceRef Ref => new(Kind, Name, Namespace);

    /// <summary>The API group, i.e. <c>networking.istio.io</c> for <c>networking.istio.io/v1</c>. Empty for core.</summary>
    public string ApiGroup => ApiVersion.Contains('/') ? ApiVersion.Split('/')[0] : string.Empty;
}
