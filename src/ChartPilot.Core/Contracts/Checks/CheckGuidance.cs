namespace ChartPilot.Core.Checks;

/// <summary>
/// One way to resolve a finding. A rule offers several, because "what should I do about this?"
/// usually has more than one defensible answer and the right one depends on the workload.
/// </summary>
/// <param name="Title">A short imperative name, e.g. "Run as a non-root user".</param>
/// <param name="Summary">One sentence: what this option actually does.</param>
/// <param name="Yaml">The configuration to add or change. Paste-ready.</param>
/// <param name="Tradeoff">
/// When to pick this one, and what it costs. The honest sentence a colleague would add — an option
/// with no trade-off stated is an option the reader cannot choose between.
/// </param>
/// <param name="IsRecommended">
/// The default answer for a service with no special constraints. Exactly one option per check
/// carries this, so a reader in a hurry has somewhere to start.
/// </param>
public sealed record FixOption(
    string Title,
    string Summary,
    string Yaml,
    string Tradeoff,
    bool IsRecommended = false);

/// <summary>
/// The plain-language half of a check: what the finding means for someone who has not met this
/// rule before, and the concrete options for resolving it.
///
/// <para>
/// This is authored per rule and shipped with ChartPilot — no model call, no network, identical in
/// the GUI, the report and the CLI. <see cref="CheckDescriptor.Rationale"/> answers "why does this
/// rule exist"; this answers "what do I do now".
/// </para>
/// </summary>
/// <param name="WhatItMeans">
/// The finding restated without jargon, for a reader who does not already know the mechanism.
/// </param>
/// <param name="Options">Two to four ways out, most-recommended first.</param>
public sealed record CheckGuidance(string WhatItMeans, IReadOnlyList<FixOption> Options);
