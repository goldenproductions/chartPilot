using ChartPilot.Core.Charts;
using ChartPilot.Core.Helm;
using ChartPilot.Core.Manifests;
using ChartPilot.Core.Profiles;
using ChartPilot.Core.Values;

namespace ChartPilot.Core.Checks;

/// <summary>
/// Everything a check is allowed to look at. The five positional members are the contract from the
/// architecture document; Chart and LintMessages are init-only extras so governance rules can see
/// chart metadata and helm lint output.
/// </summary>
public sealed record CheckContext(
    IResourceGraph Graph,
    ValuesDocument Values,
    Profile Profile,
    DataClassification Classification,
    string Environment)
{
    /// <summary>Chart metadata, when the review was entered through a chart directory rather than raw manifests.</summary>
    public ChartModel? Chart { get; init; }

    /// <summary>Where the chart declares it is reachable from (<c>platform.exposure</c>).</summary>
    public Exposure Exposure { get; init; } = Exposure.Unknown;

    /// <summary>helm lint output, folded into the findings list under CP-GOV-*.</summary>
    public IReadOnlyList<HelmLintMessage> LintMessages { get; init; } = [];

    /// <summary>
    /// True when <c>helm lint</c> actually ran. A clean chart produces no messages at all, so without
    /// this flag "lint ran and said nothing" and "lint never ran" are indistinguishable and the
    /// CP-GOV-006/007/008 rules can never be reported as passing.
    /// </summary>
    public bool LintRan { get; init; }

    /// <summary>
    /// Environment names that mean production. A chart reviewed against values-prod.yaml is held to
    /// the production bar even under a permissive profile.
    /// </summary>
    private static readonly string[] ProductionEnvironments = ["prod", "production", "prd", "live"];

    /// <summary>True when this review is of a production environment.</summary>
    public bool IsProductionEnvironment
        => Environment is { Length: > 0 }
           && ProductionEnvironments.Contains(Environment.Trim(), StringComparer.OrdinalIgnoreCase);
}
