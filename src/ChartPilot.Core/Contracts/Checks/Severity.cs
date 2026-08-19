namespace ChartPilot.Core.Checks;

/// <summary>
/// How serious a finding is. Ordered so that comparison works: Critical &gt; Warning &gt; Info.
/// A check declares a default; the resolved value comes from the profile and the data classification.
/// </summary>
public enum Severity
{
    Info = 0,
    Warning = 1,
    Critical = 2
}

/// <summary>The four scoring categories. Every check belongs to exactly one.</summary>
public enum CheckCategory
{
    Security,
    Reliability,
    Operability,
    Governance
}
