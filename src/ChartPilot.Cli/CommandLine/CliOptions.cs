using ChartPilot.Core.Checks;

namespace ChartPilot.Cli.CommandLine;

/// <summary>The commands the CLI understands.</summary>
internal enum CliCommand
{
    Help,
    Check,
    Profiles,
    Checks
}

/// <summary>
/// Exit codes are load-bearing: a pipeline has to be able to tell "the chart is bad" from
/// "the tool broke".
/// </summary>
internal static class ExitCodes
{
    public const int Clean = 0;
    public const int GateFailed = 1;
    public const int ExecutionError = 2;
}

/// <summary>A parsed command line.</summary>
internal sealed record CliOptions(
    CliCommand Command,
    string? ChartPath,
    IReadOnlyList<string> ValuesFiles,
    string? ProfileId,
    string? Environment,
    string? ReleaseName,
    string? ReportPath,
    string? WorkflowPath,
    Severity? FailOn,
    bool Json,
    /// <summary>Print what each finding means and the options for fixing it.</summary>
    bool Explain);

/// <summary>Either a parsed command line or the reason it could not be parsed.</summary>
internal sealed record ParseResult(CliOptions? Options, string? Error)
{
    public static ParseResult Ok(CliOptions options) => new(options, null);

    public static ParseResult Fail(string error) => new(null, error);
}
