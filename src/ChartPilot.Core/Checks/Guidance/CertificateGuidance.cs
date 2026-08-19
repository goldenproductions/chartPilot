namespace ChartPilot.Core.Checks.Guidance;

/// <summary>Authored guidance for the CP-CERT-* family (cert-manager).</summary>
internal static class CertificateGuidance
{
    public static IEnumerable<KeyValuePair<string, CheckGuidance>> Entries()
    {
        yield return new("CP-CERT-001", new(
            "The Certificate does not say how early to renew. cert-manager falls back to its own default, so "
            + "nobody has decided how much warning you get: renewBefore is the window in which renewal can keep "
            + "failing before the certificate actually expires and TLS stops working.",
            [
                new FixOption(
                    "Renew at a third of the lifetime",
                    "Give renewal plenty of runway relative to how long the certificate lives.",
                    "spec:\n  duration: 2160h      # 90 days\n  renewBefore: 720h    # 30 days of runway",
                    "A month of failed renewals before anything breaks is enough for a holiday and an incident. "
                    + "The common convention, and easy to defend in review.",
                    IsRecommended: true),
                new FixOption(
                    "Renew at two thirds of the lifetime",
                    "Start renewing much earlier, for a certificate you cannot afford to lose.",
                    "spec:\n  duration: 2160h      # 90 days\n  renewBefore: 1440h   # 60 days of runway",
                    "More resilient to a long-broken ACME path, at the cost of renewing more often. Sensible for "
                    + "an externally-facing certificate with a slow issuance path.",
                    false)
            ]));

        yield return new("CP-CERT-002", new(
            "The certificate lives longer than 90 days. A long lifetime means the renewal path runs once a year, "
            + "so when it is broken nobody finds out until it matters — and a leaked key stays useful to whoever "
            + "took it for just as long.",
            [
                new FixOption(
                    "Shorten it to 90 days",
                    "Make renewal a routine event rather than an annual one.",
                    "spec:\n  duration: 2160h      # 90 days\n  renewBefore: 720h    # 30 days",
                    "The de-facto standard, and what public CAs issue anyway. Frequent renewal is what proves the "
                    + "renewal path still works.",
                    IsRecommended: true),
                new FixOption(
                    "Shorter still, for internal traffic",
                    "Internal certificates issued by your own CA can rotate much faster.",
                    "spec:\n  duration: 720h       # 30 days\n  renewBefore: 240h    # 10 days",
                    "Good for mesh and service-to-service certificates where issuance is instant and local. Do not "
                    + "do this against a rate-limited public CA.",
                    false)
            ]));

        yield return new("CP-CERT-003", new(
            "The Certificate points at an issuer this chart does not render. cert-manager accepts the object, "
            + "then never issues anything: the chart installs green, the Secret is never created, and the only "
            + "sign of trouble is in cert-manager's own logs.",
            [
                new FixOption(
                    "Ship the issuer with the chart",
                    "Make the chart self-contained so installing it produces a working certificate.",
                    "apiVersion: cert-manager.io/v1\nkind: Issuer\nmetadata:\n  name: platform-internal-ca\nspec:\n  ca:\n    secretName: platform-internal-ca-key-pair",
                    "Right when the issuer belongs to this service. An Issuer is namespaced, so it lives and dies "
                    + "with the release.",
                    IsRecommended: true),
                new FixOption(
                    "Reference the platform ClusterIssuer",
                    "Use a cluster-wide issuer the platform team maintains, and say that it is one.",
                    "spec:\n  issuerRef:\n    name: letsencrypt-prod\n    kind: ClusterIssuer\n    group: cert-manager.io",
                    "The usual arrangement. Get `kind: ClusterIssuer` right — defaulting to Issuer makes "
                    + "cert-manager look in the release namespace and find nothing, which is exactly this finding.",
                    false),
                new FixOption(
                    "Document it as a prerequisite",
                    "Keep the external reference and make the dependency explicit.",
                    "# .chartpilot.yaml\nsuppress:\n  - id: CP-CERT-003\n    reason: \"letsencrypt-prod ClusterIssuer is a documented platform prerequisite - see platform runbook PR-14.\"\n    expires: 2027-01-01",
                    "Fine when the issuer really is guaranteed by the platform. Verify it exists in the target "
                    + "cluster before waiving — a missing issuer fails silently, which is the whole problem.",
                    false)
            ]));

        yield return new("CP-CERT-004", new(
            "This Certificate writes a TLS Secret that nothing in the chart references. Either a rename was only "
            + "half applied — in which case the Gateway or Ingress is still serving the old certificate — or this "
            + "is dead configuration that will be renewed forever.",
            [
                new FixOption(
                    "Reference it where TLS terminates",
                    "Point the Gateway or Ingress at the Secret this Certificate produces.",
                    "# Istio Gateway\ntls:\n  mode: SIMPLE\n  credentialName: member-api-tls\n\n# or an Ingress\ntls:\n  - secretName: member-api-tls\n    hosts: [member-api.example.com]",
                    "The usual fix: the names drifted apart. Template both from the same value so they cannot "
                    + "diverge again.",
                    IsRecommended: true),
                new FixOption(
                    "Remove the Certificate",
                    "If nothing uses it, stop issuing it.",
                    "certificate:\n  enabled: false",
                    "Deleting dead configuration is a real fix. Check first that nothing outside this chart mounts "
                    + "the Secret — cert-manager cannot tell you who reads it.",
                    false)
            ]));

        yield return new("CP-CERT-005", new(
            "renewBefore is greater than or equal to duration, which cert-manager rejects outright. The "
            + "certificate is never issued at all — this is a configuration error, not a hardening suggestion, "
            + "and TLS will simply not come up.",
            [
                new FixOption(
                    "Make renewBefore a fraction of duration",
                    "Renewal has to start inside the certificate's lifetime, not before it begins.",
                    "spec:\n  duration: 2160h      # 90 days\n  renewBefore: 720h    # 30 days - must be well under duration",
                    "Keep renewBefore at a third to two thirds of duration. Anything closer to duration means "
                    + "cert-manager renews continuously.",
                    IsRecommended: true),
                new FixOption(
                    "Lengthen the certificate instead",
                    "If the renewal window is the number you care about, raise duration to fit it.",
                    "spec:\n  duration: 2160h      # was 168h, which was shorter than renewBefore\n  renewBefore: 720h",
                    "Right when the short duration was the accident. Check the issuer will actually grant the "
                    + "longer lifetime — many CAs cap it.",
                    false)
            ]));
    }
}
