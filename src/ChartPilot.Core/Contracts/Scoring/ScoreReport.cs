using ChartPilot.Core.Checks;
using ChartPilot.Core.Profiles;

namespace ChartPilot.Core.Scoring;

/// <summary>The score for one category, plus the counts that produced it.</summary>
/// <param name="Score">0..100, i.e. clamp(0, 100, 100 - sum of deductions).</param>
public sealed record CategoryScore(
    CheckCategory Category,
    int Score,
    int CriticalCount,
    int WarningCount,
    int InfoCount,
    int PassedCount);

/// <summary>
/// The platform score: one score per category plus the weighted overall.
/// It is a conversation starter, never a pass/fail on its own — gating uses findings.
/// </summary>
public sealed record ScoreReport(int Overall, IReadOnlyList<CategoryScore> Categories)
{
    /// <summary>The score for a category, or null when the category is not present in this report.</summary>
    public CategoryScore? this[CheckCategory category]
    {
        get
        {
            foreach (var score in Categories)
            {
                if (score.Category == category)
                {
                    return score;
                }
            }

            return null;
        }
    }
}

/// <summary>
/// Turns findings and passed checks into a <see cref="ScoreReport"/>, using the deductions and
/// weights carried by the profile rather than constants in code.
/// </summary>
public interface IScorer
{
    ScoreReport Score(IReadOnlyList<Finding> findings, IReadOnlyList<PassedCheck> passed, Profile profile);
}
