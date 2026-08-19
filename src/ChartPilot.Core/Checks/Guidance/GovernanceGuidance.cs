namespace ChartPilot.Core.Checks.Guidance;

/// <summary>Authored guidance for the CP-GOV-* family.</summary>
internal static class GovernanceGuidance
{
    public static IEnumerable<KeyValuePair<string, CheckGuidance>> Entries()
    {
        yield return new("CP-GOV-001", new(
            "The chart ships no values.schema.json, so nothing validates what people put in values.yaml. A typo "
            + "in a key name does not fail — it is simply ignored, and the chart renders without the probe or the "
            + "limit that the misspelled key was supposed to set.",
            [
                new FixOption(
                    "Add a schema for the keys that matter",
                    "Validate the values whose absence would be dangerous, and leave the rest open.",
                    "{\n  \"$schema\": \"https://json-schema.org/draft/2020-12/schema\",\n  \"type\": \"object\",\n  \"required\": [\"image\", \"resources\"],\n  \"properties\": {\n    \"replicaCount\": { \"type\": \"integer\", \"minimum\": 1 },\n    \"image\": {\n      \"type\": \"object\",\n      \"required\": [\"repository\", \"tag\"],\n      \"properties\": {\n        \"repository\": { \"type\": \"string\" },\n        \"tag\": { \"type\": \"string\", \"pattern\": \"^(?!latest$).+\" }\n      }\n    }\n  }\n}",
                    "Start small — required keys and a few types catch most real mistakes. A schema also gives "
                    + "ChartPilot's values editor completion and inline validation.",
                    IsRecommended: true),
                new FixOption(
                    "Close the object to catch typos",
                    "Reject unknown keys outright, so a misspelling is an error rather than a silent no-op.",
                    "{\n  \"$schema\": \"https://json-schema.org/draft/2020-12/schema\",\n  \"type\": \"object\",\n  \"additionalProperties\": false,\n  \"properties\": {\n    \"replicaCount\": { \"type\": \"integer\" },\n    \"image\": { \"type\": \"object\" }\n  }\n}",
                    "The strictest and most useful form for catching typos. It means every key any user might set "
                    + "has to be listed, including the ones subcharts read — which is real work on a large chart.",
                    false)
            ]));

        yield return new("CP-GOV-002", new(
            "A dependency is declared with a version range rather than an exact version. Two `helm dependency "
            + "update` runs a month apart then produce different clusters from the same commit, and a rollback "
            + "does not restore what was actually running.",
            [
                new FixOption(
                    "Pin the exact version",
                    "State the version you tested, and update it deliberately.",
                    "dependencies:\n  - name: redis\n    version: 18.4.0        # not ^18.0.0\n    repository: https://charts.bitnami.com/bitnami",
                    "The answer for anything deployed to production. Pair it with a bot that opens a pull request "
                    + "when a new version appears, so pinning does not mean never upgrading.",
                    IsRecommended: true),
                new FixOption(
                    "Pin, and commit the lock file",
                    "Also commit Chart.lock so the resolved digests travel with the repository.",
                    "# commit Chart.lock alongside Chart.yaml\n# CI then uses:\nhelm dependency build ./chart    # honours Chart.lock\n# rather than:\nhelm dependency update ./chart  # re-resolves and rewrites it",
                    "Reproducible down to the digest, and it makes CI fail loudly if an upstream chart is "
                    + "retagged. `helm dependency build` is the command that respects the lock — update ignores it.",
                    false)
            ]));

        yield return new("CP-GOV-003", new(
            "No rendered resource carries ownership metadata. That is what lets a platform team route a policy "
            + "violation, a cost anomaly or a 3am page to a team — without it, everything this chart deploys is "
            + "anonymous at cluster scale.",
            [
                new FixOption(
                    "Add ownership to the common labels",
                    "Set it once in the chart's label helper so every resource inherits it.",
                    "{{- define \"member-api.labels\" -}}\napp.kubernetes.io/part-of: member-platform\napp.kubernetes.io/managed-by: {{ .Release.Service }}\n{{- end }}\n\n# plus a contactable owner as an annotation:\ncommonAnnotations:\n  chartpilot.io/owner: team-member-platform",
                    "One edit covers everything the chart renders, now and later. `part-of` is the label most "
                    + "platform tooling already groups by.",
                    IsRecommended: true),
                new FixOption(
                    "Take it from values",
                    "Make ownership a required input rather than a hard-coded string.",
                    "# values.yaml\nplatform:\n  owner: team-member-platform\n  contact: \"#team-member-platform\"\n\n# and require it in values.schema.json\n\"required\": [\"platform\"]",
                    "Better for a chart used by several teams: each deployment states its own owner, and the "
                    + "schema stops anyone skipping it.",
                    false)
            ]));

        yield return new("CP-GOV-004", new(
            "values.yaml does not declare what kind of data this service handles. That declaration is what "
            + "decides whether missing mTLS is a note or a blocker — undeclared, every downstream control has to "
            + "assume the least strict answer.",
            [
                new FixOption(
                    "Declare the classification and exposure",
                    "State what the service handles and who can reach it.",
                    "platform:\n  dataClassification: sensitive-personal-data   # public | internal | confidential | sensitive-personal-data\n  exposure: internal                             # internal | public",
                    "ChartPilot reads these and tightens its checks accordingly, so declaring it honestly makes "
                    + "the review stricter — which is the point. Under-declaring to get a better score is the one "
                    + "way to make this feature useless.",
                    IsRecommended: true),
                new FixOption(
                    "Declare it per environment",
                    "When the same chart serves both a test double and real data.",
                    "# values-dev.yaml\nplatform:\n  dataClassification: internal\n\n# values-prod.yaml\nplatform:\n  dataClassification: sensitive-personal-data",
                    "Accurate when dev genuinely holds synthetic data. Be sure that is true — a dev environment "
                    + "restored from a production dump is not internal-only.",
                    false)
            ]));

        yield return new("CP-GOV-005", new(
            "A suppression in .chartpilot.yaml has passed its expiry date, or never had a reason. Either way the "
            + "finding it was waiving is now being re-raised, because a waiver nobody revisits is not an exception "
            + "— it is a hidden violation.",
            [
                new FixOption(
                    "Fix the underlying finding",
                    "The waiver has done its job of buying time; spend it.",
                    "# delete the entry from .chartpilot.yaml and fix the finding it covered",
                    "The intended outcome. An expiry exists precisely to force this conversation rather than to be "
                    + "extended forever.",
                    IsRecommended: true),
                new FixOption(
                    "Renew it, with a current reason",
                    "If the constraint still holds, say so again and put a new date on it.",
                    "suppress:\n  - id: CP-SEC-004\n    resource: Deployment/legacy-importer\n    reason: \"Vendor image still writes to /var/lib. Upstream issue #412, fix promised for 2.0.\"\n    expires: 2027-03-01",
                    "Legitimate when the blocker is genuinely external. Rewrite the reason rather than only "
                    + "bumping the date — a stale reason is how a waiver survives its own justification.",
                    false)
            ]));

        yield return new("CP-GOV-006", new(
            "`helm lint` reported an error, which means Helm itself considers this chart malformed. Everything "
            + "downstream — the rendered manifests, the resource graph, the findings, the score — is reasoning "
            + "about a chart Helm would refuse to install.",
            [
                new FixOption(
                    "Run helm lint and fix what it names",
                    "Take Helm's own message; it points at the file and the problem.",
                    "helm lint ./chart\nhelm lint ./chart -f values-prod.yaml    # lint each environment too",
                    "Fix this before reading any other finding — a chart that does not lint cleanly makes the rest "
                    + "of the review unreliable. Linting each values file catches errors that only one environment "
                    + "triggers.",
                    IsRecommended: true)
            ]));

        yield return new("CP-GOV-007", new(
            "`helm lint` reported a warning. These are the conventions that keep a chart legible to whoever "
            + "maintains it next — missing metadata, deprecated API versions, an absent icon — cheap individually "
            + "and expensive once they accumulate.",
            [
                new FixOption(
                    "Clear the warnings",
                    "Most are one line in Chart.yaml.",
                    "# Chart.yaml\nicon: https://example.com/member-api.png\ndescription: Member API - serves member profile data\nmaintainers:\n  - name: Member Platform Team\n    email: member-platform@example.com",
                    "Quick, and it removes noise so a real warning stands out later. Deprecated apiVersion "
                    + "warnings matter more — those become errors on a future cluster.",
                    IsRecommended: true),
                new FixOption(
                    "Accept them for now",
                    "Record that the warnings are known rather than unexamined.",
                    "# .chartpilot.yaml\nsuppress:\n  - id: CP-GOV-007\n    reason: \"Chart metadata is tracked in PLAT-231; not blocking this release.\"\n    expires: 2026-12-01",
                    "Reasonable under time pressure. Read them first — an apiVersion deprecation hiding among "
                    + "cosmetic warnings is the one that breaks a cluster upgrade.",
                    false)
            ]));

        yield return new("CP-GOV-008", new(
            "`helm lint` printed an informational message. It is carried into the review so that the report is "
            + "the whole picture and a reviewer does not have to run Helm separately to see what Helm said.",
            [
                new FixOption(
                    "Read it and decide",
                    "Informational messages are usually suggestions rather than problems.",
                    "helm lint ./chart",
                    "No action is required for the score — these do not deduct. Worth a glance, because Helm "
                    + "sometimes mentions something you were about to trip over.",
                    IsRecommended: true)
            ]));
    }
}
