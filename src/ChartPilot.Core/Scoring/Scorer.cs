using ChartPilot.Core.Checks;
using ChartPilot.Core.Profiles;

namespace ChartPilot.Core.Scoring;

/// <summary>
/// The platform score, exactly as architecture.md 5.5 defines it:
/// <code>
/// categoryScore = clamp(0, 100, 100 - sum of deductions)   Critical 25, Warning 8, Info 0
/// overall       = 0.35*Security + 0.30*Reliability + 0.20*Operability + 0.15*Governance
/// </code>
/// <para>
/// Deductions and weights come from the profile rather than from constants here, so an organization
/// can tune the gate without a rebuild. Suppressed findings do not deduct — that is the entire point
/// of an accepted, justified exception — while expired ones are back in the findings list and do.
/// </para>
/// </summary>
public sealed class Scorer : IScorer
{
    /// <summary>Every category appears in every report, including the ones with nothing to say.</summary>
    private static readonly CheckCategory[] AllCategories =
    [
        CheckCategory.Security,
        CheckCategory.Reliability,
        CheckCategory.Operability,
        CheckCategory.Governance
    ];

    private readonly ICheckCatalog? _catalog;

    /// <summary>
    /// The catalog is optional so the scorer stays usable as a pure function in tests, but the API
    /// and CLI always supply it: a descriptor is the authoritative answer to "which category is this
    /// finding in", and the id prefix is only a fallback.
    /// </summary>
    public Scorer(ICheckCatalog? catalog = null) => _catalog = catalog;

    public ScoreReport Score(IReadOnlyList<Finding> findings, IReadOnlyList<PassedCheck> passed, Profile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        findings ??= [];
        passed ??= [];

        var deductions = profile.Deductions ?? new SeverityDeductions();
        var weights = profile.Weights ?? new ScoreWeights();

        // A finding names a check id, not a category; the category lives on the descriptor. The
        // catalog answers that authoritatively, and the passed list covers the rest.
        var categoryOfCheck = BuildCategoryIndex(passed);

        var counts = AllCategories.ToDictionary(category => category, _ => new Counters());

        foreach (var finding in findings)
        {
            var category = CategoryOf(finding.CheckId, categoryOfCheck);
            var counter = counts[category];

            switch (finding.Severity)
            {
                case Severity.Critical:
                    counter.Critical++;
                    break;
                case Severity.Warning:
                    counter.Warning++;
                    break;
                default:
                    counter.Info++;
                    break;
            }
        }

        foreach (var check in passed)
        {
            counts[check.Category].Passed++;
        }

        var categories = new List<CategoryScore>(AllCategories.Length);

        foreach (var category in AllCategories)
        {
            var counter = counts[category];

            var deducted = (counter.Critical * deductions.Critical)
                           + (counter.Warning * deductions.Warning)
                           + (counter.Info * deductions.Info);

            var score = Math.Clamp(100 - deducted, 0, 100);

            categories.Add(new CategoryScore(
                category,
                score,
                counter.Critical,
                counter.Warning,
                counter.Info,
                counter.Passed));
        }

        var overall = (weights.Security * ScoreOf(categories, CheckCategory.Security))
                      + (weights.Reliability * ScoreOf(categories, CheckCategory.Reliability))
                      + (weights.Operability * ScoreOf(categories, CheckCategory.Operability))
                      + (weights.Governance * ScoreOf(categories, CheckCategory.Governance));

        var rounded = (int)Math.Round(overall, MidpointRounding.AwayFromZero);

        return new ScoreReport(Math.Clamp(rounded, 0, 100), categories);
    }

    private static int ScoreOf(IReadOnlyList<CategoryScore> categories, CheckCategory category)
    {
        foreach (var entry in categories)
        {
            if (entry.Category == category)
            {
                return entry.Score;
            }
        }

        return 100;
    }

    private static Dictionary<string, CheckCategory> BuildCategoryIndex(IReadOnlyList<PassedCheck> passed)
    {
        var index = new Dictionary<string, CheckCategory>(StringComparer.OrdinalIgnoreCase);

        foreach (var check in passed)
        {
            index[check.CheckId] = check.Category;
        }

        return index;
    }

    /// <summary>
    /// The category of a check id: the catalog descriptor first, then a check that passed under the
    /// same id, then the id prefix — which is precisely why the rule id format is a contract.
    /// </summary>
    internal CheckCategory CategoryOf(string checkId, IReadOnlyDictionary<string, CheckCategory>? index = null)
    {
        if (_catalog?.Find(checkId) is { } descriptor)
        {
            return descriptor.Category;
        }

        if (index is not null && index.TryGetValue(checkId, out var known))
        {
            return known;
        }

        return CategoryOfPrefix(checkId);
    }

    /// <summary>Maps a rule id prefix onto its scoring category.</summary>
    internal static CheckCategory CategoryOfPrefix(string checkId)
    {
        if (string.IsNullOrWhiteSpace(checkId))
        {
            return CheckCategory.Governance;
        }

        var id = checkId.Trim();

        if (id.StartsWith("CP-SEC-", StringComparison.OrdinalIgnoreCase))
        {
            return CheckCategory.Security;
        }

        if (id.StartsWith("CP-REL-", StringComparison.OrdinalIgnoreCase))
        {
            return CheckCategory.Reliability;
        }

        if (id.StartsWith("CP-GOV-", StringComparison.OrdinalIgnoreCase))
        {
            return CheckCategory.Governance;
        }

        // CP-NET-* straddles security and operability; CP-CERT-* and CP-OBS-* are operability.
        if (id.StartsWith("CP-NET-", StringComparison.OrdinalIgnoreCase))
        {
            return CheckCategory.Security;
        }

        return CheckCategory.Operability;
    }

    private sealed class Counters
    {
        public int Critical;
        public int Warning;
        public int Info;
        public int Passed;
    }
}
