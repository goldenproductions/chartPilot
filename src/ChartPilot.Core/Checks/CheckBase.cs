using ChartPilot.Core.Manifests;

namespace ChartPilot.Core.Checks;

/// <summary>
/// Convenience base for the rule catalog.
/// <para>
/// <b>Severity contract:</b> a rule always emits its own <see cref="CheckDescriptor.DefaultSeverity"/>.
/// It never inspects the profile or the data classification to decide how bad the finding is. The
/// engine re-writes <see cref="Finding.Severity"/> through <c>ISeverityResolver</c> after the rule
/// returns, which is what lets one catalog serve every golden path profile (architecture.md 5.3).
/// </para>
/// </summary>
internal abstract class CheckBase : IResourceCheck
{
    public abstract CheckDescriptor Descriptor { get; }

    public abstract IEnumerable<Finding> Evaluate(CheckContext context);

    /// <summary>A finding against a specific rendered resource.</summary>
    protected Finding Violation(RenderedResource resource, string message, string? yamlPath = null)
        => new(
            Descriptor.Id,
            Descriptor.DefaultSeverity,
            ResourceRef.From(resource),
            message,
            Descriptor.Remediation,
            yamlPath,
            resource.SourceTemplate);

    /// <summary>A finding about the chart as a whole rather than about one resource.</summary>
    protected Finding ChartViolation(string message)
        => new(Descriptor.Id, Descriptor.DefaultSeverity, null, message, Descriptor.Remediation);
}

/// <summary>Base for rules that only apply when the chart renders at least one pod-carrying workload.</summary>
internal abstract class WorkloadCheckBase : CheckBase, IConditionalCheck
{
    public virtual bool IsApplicable(CheckContext context) => CheckHelpers.HasWorkloads(context);
}

/// <summary>Base for rules that only apply to long-lived workloads (Deployment, StatefulSet, DaemonSet, ReplicaSet).</summary>
internal abstract class LongLivedWorkloadCheckBase : CheckBase, IConditionalCheck
{
    public virtual bool IsApplicable(CheckContext context) => CheckHelpers.HasLongLivedWorkloads(context);
}

/// <summary>Base for the Istio family: not reported at all on a chart that ships no mesh resources.</summary>
internal abstract class IstioCheckBase : CheckBase, IConditionalCheck
{
    public virtual bool IsApplicable(CheckContext context) => CheckHelpers.HasIstio(context);
}

/// <summary>Base for the cert-manager family: only applicable when the chart renders a Certificate.</summary>
internal abstract class CertificateCheckBase : CheckBase, IConditionalCheck
{
    public virtual bool IsApplicable(CheckContext context) => context.Graph.ContainsKind("Certificate");
}

/// <summary>Base for rules that read chart metadata rather than rendered manifests.</summary>
internal abstract class ChartMetadataCheckBase : CheckBase, IConditionalCheck
{
    public virtual bool IsApplicable(CheckContext context) => context.Chart is not null;
}
