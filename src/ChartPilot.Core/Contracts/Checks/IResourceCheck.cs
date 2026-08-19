namespace ChartPilot.Core.Checks;

/// <summary>
/// One rule. Implementations are pure functions of the context: no disk, no process, no clock.
/// Adding a check is adding one implementation of this interface plus a fixture test pair.
/// </summary>
public interface IResourceCheck
{
    CheckDescriptor Descriptor { get; }

    /// <summary>
    /// Returns one finding per violation. Return nothing when the rule is satisfied, or when the
    /// rule does not apply to this chart at all.
    /// </summary>
    IEnumerable<Finding> Evaluate(CheckContext context);
}

/// <summary>The registered rule catalog, exposed to the GUI through GET /api/v1/checks.</summary>
public interface ICheckCatalog
{
    IReadOnlyList<CheckDescriptor> Descriptors { get; }

    IReadOnlyList<IResourceCheck> Checks { get; }

    CheckDescriptor? Find(string checkId);
}

/// <summary>The output of one full pass over the catalog.</summary>
public sealed record CheckRunResult(
    IReadOnlyList<Finding> Findings,
    IReadOnlyList<PassedCheck> Passed,
    IReadOnlyList<SuppressedFinding> Suppressed);

/// <summary>Runs the catalog against a context, resolving severities and applying suppressions.</summary>
public interface ICheckEngine
{
    CheckRunResult Run(CheckContext context, IReadOnlyList<Suppression> suppressions);
}
