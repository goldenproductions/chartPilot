using ChartPilot.Core.Profiles;

namespace ChartPilot.Core.Checks;

/// <summary>
/// Runs the catalog over one context.
/// <para>
/// The engine owns three things the rules deliberately do not: which rules the profile has turned
/// off, what severity a finding actually has, and whether a finding has been waived. A rule stays a
/// pure "is this true of this chart" predicate, which is what keeps the catalog testable.
/// </para>
/// </summary>
public sealed class CheckEngine : ICheckEngine
{
    /// <summary>The rule the engine raises for suppressions that are expired or missing a reason.</summary>
    public const string SuppressionHygieneCheckId = "CP-GOV-005";

    private readonly ICheckCatalog _catalog;
    private readonly ISeverityResolver _severityResolver;
    private readonly TimeProvider _timeProvider;

    public CheckEngine(ICheckCatalog catalog, ISeverityResolver severityResolver, TimeProvider? timeProvider = null)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _severityResolver = severityResolver ?? throw new ArgumentNullException(nameof(severityResolver));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public CheckRunResult Run(CheckContext context, IReadOnlyList<Suppression> suppressions)
    {
        ArgumentNullException.ThrowIfNull(context);

        suppressions ??= [];

        var today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);
        var profile = context.Profile;
        var disabled = new HashSet<string>(profile.DisabledChecks ?? [], StringComparer.OrdinalIgnoreCase);

        var active = new List<Suppression>();
        var invalid = new List<(Suppression Suppression, string Problem)>();

        foreach (var suppression in suppressions)
        {
            if (suppression is null)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(suppression.Reason))
            {
                invalid.Add((suppression, "has no reason"));
            }
            else if (suppression.IsExpired(today))
            {
                invalid.Add((suppression, $"expired on {suppression.Expires:yyyy-MM-dd}"));
            }
            else
            {
                active.Add(suppression);
            }
        }

        var findings = new List<Finding>();
        var passed = new List<PassedCheck>();
        var suppressed = new List<SuppressedFinding>();

        foreach (var check in _catalog.Checks)
        {
            var descriptor = check.Descriptor;

            if (disabled.Contains(descriptor.Id))
            {
                continue;
            }

            if (check is IConditionalCheck conditional && !conditional.IsApplicable(context))
            {
                continue;
            }

            var severity = _severityResolver.Resolve(descriptor, profile, context.Classification);
            var produced = 0;

            foreach (var raw in check.Evaluate(context) ?? Enumerable.Empty<Finding>())
            {
                if (raw is null)
                {
                    continue;
                }

                produced++;

                // Rules emit their default severity; the resolved value is applied here.
                var finding = raw with { Severity = severity };
                var match = FindSuppression(active, finding);

                if (match is not null)
                {
                    suppressed.Add(new SuppressedFinding(finding, match.Reason, match.Expires));
                }
                else
                {
                    findings.Add(finding);
                }
            }

            if (produced == 0)
            {
                passed.Add(new PassedCheck(descriptor.Id, descriptor.Title, descriptor.Category));
            }
        }

        AppendSuppressionHygiene(findings, passed, invalid, suppressions.Count, profile, context, disabled);

        return new CheckRunResult(findings, passed, suppressed);
    }

    private void AppendSuppressionHygiene(
        List<Finding> findings,
        List<PassedCheck> passed,
        IReadOnlyList<(Suppression Suppression, string Problem)> invalid,
        int suppressionCount,
        Profile profile,
        CheckContext context,
        HashSet<string> disabled)
    {
        if (suppressionCount == 0 || disabled.Contains(SuppressionHygieneCheckId))
        {
            return;
        }

        var descriptor = _catalog.Find(SuppressionHygieneCheckId);
        if (descriptor is null)
        {
            return;
        }

        if (invalid.Count == 0)
        {
            passed.Add(new PassedCheck(descriptor.Id, descriptor.Title, descriptor.Category));
            return;
        }

        var severity = _severityResolver.Resolve(descriptor, profile, context.Classification);

        foreach (var (suppression, problem) in invalid)
        {
            var scope = suppression.Resource is { Length: > 0 } resource ? $" for {resource}" : string.Empty;

            findings.Add(new Finding(
                descriptor.Id,
                severity,
                null,
                $"Suppression of {suppression.Id}{scope} {problem}, so the finding it waived has been re-raised.",
                descriptor.Remediation));
        }
    }

    private static Suppression? FindSuppression(IReadOnlyList<Suppression> active, Finding finding)
    {
        foreach (var suppression in active)
        {
            if (!string.Equals(suppression.Id, finding.CheckId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(suppression.Resource))
            {
                return suppression;
            }

            if (finding.Resource is { } resource
                && string.Equals(suppression.Resource.Trim(), resource.Key, StringComparison.OrdinalIgnoreCase))
            {
                return suppression;
            }
        }

        return null;
    }
}
