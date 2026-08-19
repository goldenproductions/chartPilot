using ChartPilot.Core.Helm;
using ChartPilot.Core.Manifests;

namespace ChartPilot.Core.Checks;

// The CP-GOV-* family: is this chart maintainable by somebody other than its author?
// Every rule emits Descriptor.DefaultSeverity; the engine resolves the real severity.

/// <summary>CP-GOV-001 — the chart ships no values.schema.json.</summary>
internal sealed class ValuesSchemaCheck : ChartMetadataCheckBase
{
    public override CheckDescriptor Descriptor { get; } = new(
        "CP-GOV-001",
        "Chart has no values.schema.json",
        CheckCategory.Governance,
        Severity.Warning,
        "Without a schema, a typo in values.yaml renders a chart that is silently missing a probe or a limit. A "
        + "schema turns that into an install-time error, and it is what lets a GUI offer guided editing at all.",
        "Add values.schema.json next to Chart.yaml:\n"
        + "{\n  \"$schema\": \"https://json-schema.org/draft/2020-12/schema\",\n  \"type\": \"object\",\n"
        + "  \"required\": [\"image\", \"resources\"],\n  \"properties\": {\n"
        + "    \"replicaCount\": { \"type\": \"integer\", \"minimum\": 1 }\n  }\n}",
        "https://helm.sh/docs/topics/charts/#schema-files");

    public override IEnumerable<Finding> Evaluate(CheckContext context)
    {
        if (context.Chart is { HasValuesSchema: false } chart)
        {
            yield return ChartViolation($"Chart '{chart.Name}' does not ship a values.schema.json.");
        }
    }
}

/// <summary>CP-GOV-002 — a dependency pinned to a range rather than a version.</summary>
internal sealed class PinnedDependencyCheck : ChartMetadataCheckBase
{
    public override CheckDescriptor Descriptor { get; } = new(
        "CP-GOV-002",
        "Chart dependency version is not pinned",
        CheckCategory.Governance,
        Severity.Warning,
        "A range such as ^18.0.0 means two `helm dependency update` runs a month apart produce different clusters "
        + "from the same commit. Pinning is what makes a release reproducible and a rollback meaningful.",
        "dependencies:\n  - name: redis\n    version: 18.4.0   # exact, not ^18.0.0\n"
        + "    repository: https://charts.bitnami.com/bitnami",
        "https://helm.sh/docs/helm/helm_dependency/");

    public override bool IsApplicable(CheckContext context) => context.Chart is { Dependencies.Count: > 0 };

    public override IEnumerable<Finding> Evaluate(CheckContext context)
    {
        if (context.Chart is not { } chart)
        {
            yield break;
        }

        foreach (var dependency in chart.Dependencies)
        {
            if (!dependency.IsVersionPinned)
            {
                yield return ChartViolation(
                    $"Dependency '{dependency.Name}' has version '{dependency.Version ?? "(none)"}', which is not an exact version.");
            }
        }
    }
}

/// <summary>CP-GOV-003 — nothing in the chart says who owns it.</summary>
internal sealed class ChartOwnershipCheck : CheckBase, IConditionalCheck
{
    public override CheckDescriptor Descriptor { get; } = new(
        "CP-GOV-003",
        "Chart resources declare no ownership metadata",
        CheckCategory.Governance,
        Severity.Warning,
        "Ownership metadata is what lets a platform team route a policy violation, a cost anomaly or an incident to "
        + "a team instead of broadcasting it. A chart where no resource declares it cannot be governed at scale.",
        "Add ownership to the common labels of every resource:\n"
        + "metadata:\n  labels:\n    app.kubernetes.io/part-of: my-platform\n"
        + "  annotations:\n    chartpilot.io/owner: team-member-platform",
        "https://kubernetes.io/docs/concepts/overview/working-with-objects/common-labels/");

    public bool IsApplicable(CheckContext context) => context.Graph.Resources.Count > 0;

    public override IEnumerable<Finding> Evaluate(CheckContext context)
    {
        foreach (var resource in context.Graph.Resources)
        {
            var labels = ManifestNavigator.GetLabels(resource);
            var annotations = ManifestNavigator.GetAnnotations(resource);

            if (CheckHelpers.OwnershipLabels.Any(labels.ContainsKey)
                || CheckHelpers.OwnershipAnnotations.Any(annotations.ContainsKey))
            {
                yield break;
            }
        }

        yield return ChartViolation(
            "No rendered resource carries an ownership label or annotation "
            + $"({string.Join(", ", CheckHelpers.OwnershipLabels)}).");
    }
}

/// <summary>CP-GOV-004 — the chart does not say how sensitive its data is.</summary>
internal sealed class DataClassificationDeclaredCheck : CheckBase
{
    public override CheckDescriptor Descriptor { get; } = new(
        "CP-GOV-004",
        "Data classification is not declared",
        CheckCategory.Governance,
        Severity.Warning,
        "The data classification is what decides whether missing mTLS is a note or a blocker. Undeclared means "
        + "every downstream control has to assume the least-strict answer, which is exactly the wrong default.",
        "values.yaml:\nplatform:\n  dataClassification: sensitive-personal-data   "
        + "# public | internal | confidential | sensitive-personal-data\n  exposure: internal",
        null);

    public override IEnumerable<Finding> Evaluate(CheckContext context)
    {
        var declared = context.Values.GetString("platform.dataClassification");

        if (string.IsNullOrWhiteSpace(declared))
        {
            yield return ChartViolation("values.yaml does not declare platform.dataClassification.");
        }
    }
}

/// <summary>
/// CP-GOV-005 — a suppression in .chartpilot.yaml that is no longer valid.
/// <para>
/// This rule has no <see cref="Evaluate"/> body of its own: the engine owns the suppression list, so
/// the engine raises these findings while applying suppressions (architecture.md 5.4). The class
/// exists so the id, rationale and remediation live in the catalog like every other rule and are
/// served by GET /checks.
/// </para>
/// </summary>
internal sealed class SuppressionHygieneCheck : CheckBase, IConditionalCheck
{
    public override CheckDescriptor Descriptor { get; } = new(
        "CP-GOV-005",
        "Suppression is expired or has no reason",
        CheckCategory.Governance,
        Severity.Warning,
        "A suppression with an expiry is a tracked exception; one that has quietly outlived its date is just a "
        + "hidden violation. Re-raising it is what stops .chartpilot.yaml from becoming a permanent blindfold.",
        ".chartpilot.yaml:\nsuppress:\n  - id: CP-SEC-004\n    resource: Deployment/legacy-importer\n"
        + "    reason: \"Vendor image requires a writable root filesystem; tracked in PLAT-412\"\n"
        + "    expires: 2027-06-01   # renew the date or remove the entry",
        null);

    /// <summary>Never evaluated directly — the engine raises CP-GOV-005 from the suppression list.</summary>
    public bool IsApplicable(CheckContext context) => false;

    public override IEnumerable<Finding> Evaluate(CheckContext context) => [];
}

/// <summary>Shared body for the three rules that fold <c>helm lint</c> output into findings.</summary>
internal abstract class HelmLintCheckBase : CheckBase, IConditionalCheck
{
    protected abstract HelmLintSeverity LintSeverity { get; }

    /// <summary>
    /// Applicable whenever lint actually ran, so a chart that lints clean is reported as passing
    /// rather than silently skipped. Keying this off the message list instead would make "lint was
    /// clean" and "lint never ran" produce identical output.
    /// </summary>
    public bool IsApplicable(CheckContext context) => context.LintRan;

    public override IEnumerable<Finding> Evaluate(CheckContext context)
    {
        foreach (var message in context.LintMessages)
        {
            if (message.Severity == LintSeverity)
            {
                yield return ChartViolation($"helm lint: {message.File}: {message.Message}");
            }
        }
    }
}

/// <summary>CP-GOV-006 — helm lint reported an error.</summary>
internal sealed class HelmLintErrorCheck : HelmLintCheckBase
{
    protected override HelmLintSeverity LintSeverity => HelmLintSeverity.Error;

    public override CheckDescriptor Descriptor { get; } = new(
        "CP-GOV-006",
        "helm lint reported an error",
        CheckCategory.Governance,
        Severity.Critical,
        "A lint error means the chart is malformed against Helm's own rules. Everything downstream — rendering, "
        + "the resource graph, the score — is reasoning about a chart that Helm itself considers broken.",
        "Run `helm lint ./chart` locally and fix the reported file before reviewing anything else.",
        "https://helm.sh/docs/helm/helm_lint/");
}

/// <summary>CP-GOV-007 — helm lint reported a warning.</summary>
internal sealed class HelmLintWarningCheck : HelmLintCheckBase
{
    protected override HelmLintSeverity LintSeverity => HelmLintSeverity.Warning;

    public override CheckDescriptor Descriptor { get; } = new(
        "CP-GOV-007",
        "helm lint reported a warning",
        CheckCategory.Governance,
        Severity.Warning,
        "Helm's warnings are the conventions that make a chart legible to the next maintainer — icons, missing "
        + "metadata, deprecated API versions. They are cheap to fix and expensive to accumulate.",
        "Run `helm lint ./chart` locally and address the reported file.",
        "https://helm.sh/docs/helm/helm_lint/");
}

/// <summary>CP-GOV-008 — helm lint reported an informational message.</summary>
internal sealed class HelmLintInfoCheck : HelmLintCheckBase
{
    protected override HelmLintSeverity LintSeverity => HelmLintSeverity.Info;

    public override CheckDescriptor Descriptor { get; } = new(
        "CP-GOV-008",
        "helm lint reported an informational message",
        CheckCategory.Governance,
        Severity.Info,
        "Informational lint output is carried through so the review report is the whole picture: a reviewer should "
        + "not have to run Helm separately to see what Helm said about the chart.",
        "Run `helm lint ./chart` locally to see the message in context.",
        "https://helm.sh/docs/helm/helm_lint/");
}
