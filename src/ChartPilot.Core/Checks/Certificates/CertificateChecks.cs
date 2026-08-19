using ChartPilot.Core.Manifests;
using YamlDotNet.RepresentationModel;

namespace ChartPilot.Core.Checks;

// The CP-CERT-* family: cert-manager resources. A certificate that fails to renew is an outage with
// a date on it. Every rule emits Descriptor.DefaultSeverity; the engine resolves the real severity.

/// <summary>CP-CERT-001 — no renewBefore, so renewal happens at cert-manager's default window.</summary>
internal sealed class CertificateRenewBeforeCheck : CertificateCheckBase
{
    public override CheckDescriptor Descriptor { get; } = new(
        "CP-CERT-001",
        "Certificate has no renewBefore",
        CheckCategory.Operability,
        Severity.Warning,
        "renewBefore is the only slack you get: it is how long the renewal can keep failing before the certificate "
        + "actually expires. Leaving it implicit means nobody has decided how much warning the on-call gets.",
        "spec:\n  duration: 2160h    # 90 days\n  renewBefore: 720h  # 30 days of runway before expiry",
        "https://cert-manager.io/docs/usage/certificate/");

    public override IEnumerable<Finding> Evaluate(CheckContext context)
    {
        foreach (var certificate in context.Graph.ByKind("Certificate"))
        {
            if (ManifestNavigator.GetString(certificate.Root, "spec.renewBefore") is null)
            {
                yield return Violation(
                    certificate,
                    $"Certificate/{certificate.Name} does not set spec.renewBefore.",
                    "spec.renewBefore");
            }
        }
    }
}

/// <summary>CP-CERT-002 — a certificate lifetime longer than 90 days.</summary>
internal sealed class CertificateDurationCheck : CertificateCheckBase
{
    /// <summary>The 90-day ceiling the public Web PKI has converged on.</summary>
    private static readonly TimeSpan MaximumDuration = TimeSpan.FromHours(2160);

    public override CheckDescriptor Descriptor { get; } = new(
        "CP-CERT-002",
        "Certificate duration is longer than 90 days",
        CheckCategory.Operability,
        Severity.Warning,
        "A long-lived certificate means the renewal path is exercised once a year, so it is broken and nobody "
        + "knows. Short lifetimes turn renewal into a routine event instead of an annual incident.",
        "spec:\n  duration: 2160h    # 90 days\n  renewBefore: 720h",
        "https://cert-manager.io/docs/usage/certificate/");

    public override IEnumerable<Finding> Evaluate(CheckContext context)
    {
        foreach (var certificate in context.Graph.ByKind("Certificate"))
        {
            var raw = ManifestNavigator.GetString(certificate.Root, "spec.duration");
            var duration = CheckHelpers.ParseGoDuration(raw);

            if (duration is { } value && value > MaximumDuration)
            {
                yield return Violation(
                    certificate,
                    $"Certificate/{certificate.Name} has duration {raw} ({CheckHelpers.DescribeDuration(value)}), "
                    + "which exceeds the 90 day ceiling.",
                    "spec.duration");
            }
        }
    }
}

/// <summary>CP-CERT-003 — the issuer the certificate names is not part of the chart.</summary>
internal sealed class CertificateIssuerCheck : CertificateCheckBase
{
    public override CheckDescriptor Descriptor { get; } = new(
        "CP-CERT-003",
        "Certificate references an issuer the chart does not render",
        CheckCategory.Operability,
        Severity.Warning,
        "cert-manager accepts a Certificate whose issuerRef does not resolve and then never issues anything. The "
        + "chart installs green, TLS never comes up, and the failure only appears in cert-manager's own logs.",
        "Ship the issuer with the chart:\n"
        + "apiVersion: cert-manager.io/v1\nkind: Issuer\nmetadata:\n  name: letsencrypt-prod\n"
        + "# or confirm the ClusterIssuer is a documented platform prerequisite of this chart.",
        "https://cert-manager.io/docs/concepts/issuer/");

    public override IEnumerable<Finding> Evaluate(CheckContext context)
    {
        foreach (var certificate in context.Graph.ByKind("Certificate"))
        {
            var name = ManifestNavigator.GetString(certificate.Root, "spec.issuerRef.name");
            if (string.IsNullOrWhiteSpace(name))
            {
                yield return Violation(
                    certificate,
                    $"Certificate/{certificate.Name} declares no spec.issuerRef.name.",
                    "spec.issuerRef");
                continue;
            }

            var kind = ManifestNavigator.GetString(certificate.Root, "spec.issuerRef.kind") ?? "Issuer";

            // The graph resolved the issuerRef into an "issued-by" edge; an edge whose target does not
            // resolve is precisely the dangling reference this rule reports.
            var resolved = CheckHelpers
                .OutgoingEdges(context, certificate, GraphRelations.IssuedBy)
                .Any(edge => context.Graph.Resolve(edge.To) is not null);

            if (!resolved)
            {
                yield return Violation(
                    certificate,
                    $"Certificate/{certificate.Name} references {kind}/{name}, which this chart does not render.",
                    "spec.issuerRef.name");
            }
        }
    }
}

/// <summary>CP-CERT-004 — a certificate whose TLS secret nothing consumes.</summary>
internal sealed class DanglingTlsSecretCheck : CertificateCheckBase
{
    public override CheckDescriptor Descriptor { get; } = new(
        "CP-CERT-004",
        "Certificate writes a TLS secret nothing references",
        CheckCategory.Operability,
        Severity.Info,
        "An unreferenced TLS secret is either a rename that was only half applied — in which case the Ingress or "
        + "Gateway is still serving the old certificate — or dead configuration that will be renewed forever.",
        "Reference the secret from the resource that terminates TLS:\n"
        + "# Gateway\ntls:\n  credentialName: my-app-tls\n# Ingress\ntls:\n  - secretName: my-app-tls\n    hosts: [my-app.example.com]",
        "https://cert-manager.io/docs/usage/certificate/");

    public override IEnumerable<Finding> Evaluate(CheckContext context)
    {
        var referenced = CollectSecretReferences(context);

        foreach (var certificate in context.Graph.ByKind("Certificate"))
        {
            var secretName = ManifestNavigator.GetString(certificate.Root, "spec.secretName");

            if (string.IsNullOrWhiteSpace(secretName))
            {
                continue;
            }

            if (!referenced.Contains(secretName))
            {
                yield return Violation(
                    certificate,
                    $"Certificate/{certificate.Name} writes secret '{secretName}', which no Ingress, Gateway or workload references.",
                    "spec.secretName");
            }
        }
    }

    private static HashSet<string> CollectSecretReferences(CheckContext context)
    {
        var referenced = new HashSet<string>(StringComparer.Ordinal);

        foreach (var ingress in context.Graph.ByKind("Ingress"))
        {
            foreach (var entry in ManifestNavigator.GetSequence(ingress.Root, "spec.tls"))
            {
                Add(ManifestNavigator.GetString(entry, "secretName"));
            }
        }

        foreach (var gateway in context.Graph.ByKind("Gateway"))
        {
            foreach (var server in ManifestNavigator.GetSequence(gateway.Root, "spec.servers"))
            {
                Add(ManifestNavigator.GetString(server, "tls.credentialName"));
                Add(ManifestNavigator.GetString(server, "tls.secretName"));
            }
        }

        foreach (var rule in context.Graph.ByKind("DestinationRule"))
        {
            Add(ManifestNavigator.GetString(rule.Root, "spec.trafficPolicy.tls.credentialName"));
        }

        foreach (var workload in CheckHelpers.Workloads(context))
        {
            var podSpec = ManifestNavigator.GetPodSpec(workload);

            foreach (var volume in ManifestNavigator.GetSequence(podSpec, "volumes"))
            {
                Add(ManifestNavigator.GetString(volume, "secret.secretName"));

                foreach (var source in ManifestNavigator.GetSequence(volume, "projected.sources"))
                {
                    Add(ManifestNavigator.GetString(source, "secret.name"));
                }
            }

            foreach (var container in ManifestNavigator.GetContainers(workload))
            {
                foreach (var env in ManifestNavigator.GetSequence(container.Node, "env"))
                {
                    Add(ManifestNavigator.GetString(env, "valueFrom.secretKeyRef.name"));
                }

                foreach (var envFrom in ManifestNavigator.GetSequence(container.Node, "envFrom"))
                {
                    Add(ManifestNavigator.GetString(envFrom, "secretRef.name"));
                }
            }
        }

        return referenced;

        void Add(string? name)
        {
            if (!string.IsNullOrWhiteSpace(name))
            {
                referenced.Add(name);
            }
        }
    }
}

/// <summary>CP-CERT-005 — renewBefore is at least as long as the certificate lives.</summary>
internal sealed class RenewBeforeExceedsDurationCheck : CertificateCheckBase
{
    public override CheckDescriptor Descriptor { get; } = new(
        "CP-CERT-005",
        "renewBefore is not shorter than duration",
        CheckCategory.Operability,
        Severity.Critical,
        "cert-manager rejects a Certificate whose renewBefore is greater than or equal to its duration, so the "
        + "certificate is never issued at all. This is a config error that only surfaces after deployment.",
        "spec:\n  duration: 2160h    # 90 days\n  renewBefore: 720h  # must be clearly shorter than duration",
        "https://cert-manager.io/docs/usage/certificate/");

    public override IEnumerable<Finding> Evaluate(CheckContext context)
    {
        foreach (var certificate in context.Graph.ByKind("Certificate"))
        {
            var duration = CheckHelpers.ParseGoDuration(ManifestNavigator.GetString(certificate.Root, "spec.duration"));
            var renewBefore = CheckHelpers.ParseGoDuration(ManifestNavigator.GetString(certificate.Root, "spec.renewBefore"));

            if (duration is { } d && renewBefore is { } r && r >= d)
            {
                yield return Violation(
                    certificate,
                    $"Certificate/{certificate.Name} has renewBefore ({CheckHelpers.DescribeDuration(r)}) "
                    + $"greater than or equal to duration ({CheckHelpers.DescribeDuration(d)}).",
                    "spec.renewBefore");
            }
        }
    }
}
