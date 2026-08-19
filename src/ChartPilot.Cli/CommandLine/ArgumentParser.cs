using ChartPilot.Core.Checks;

namespace ChartPilot.Cli.CommandLine;

/// <summary>
/// Hand-rolled argument parsing. A CLI with three commands and eight flags does not need a parsing
/// library, and not taking one keeps an offline restore reproducible.
/// </summary>
internal static class ArgumentParser
{
    public const string Usage = """
        chartpilot — review Helm charts before they reach a cluster.

        Usage:
          chartpilot check <chartPath> [options]
          chartpilot profiles
          chartpilot checks

        Options for check:
          -f, --values <file>     Values file to layer. Repeatable; later files win.
              --profile <id>      Golden path profile. Defaults to the built-in default profile.
              --environment <n>   Environment label carried into the checks and the report.
              --release <name>    Release name for helm template. Defaults to the chart name.
              --report <path>     Write the Markdown review report to <path>.
              --workflow <path>   Write a generated GitHub Actions workflow to <path>.
              --fail-on <level>   info | warning | critical. Exit 1 when a finding reaches <level>.
              --json              Emit the review as JSON instead of the human summary.
          -h, --help              Show this help.

        Exit codes:
          0  no finding reached the --fail-on level
          1  the --fail-on gate tripped
          2  execution error (chart missing, helm missing, render failure)
        """;

    public static ParseResult Parse(string[] args)
    {
        if (args.Length == 0 || IsHelpFlag(args[0]))
        {
            return ParseResult.Ok(Empty(CliCommand.Help));
        }

        var command = args[0].ToLowerInvariant() switch
        {
            "check" => CliCommand.Check,
            "profiles" => CliCommand.Profiles,
            "checks" => CliCommand.Checks,
            "help" => CliCommand.Help,
            _ => (CliCommand?)null
        };

        if (command is null)
        {
            return ParseResult.Fail($"Unknown command '{args[0]}'.");
        }

        if (command is CliCommand.Help)
        {
            return ParseResult.Ok(Empty(CliCommand.Help));
        }

        string? chartPath = null;
        var valuesFiles = new List<string>();
        string? profileId = null;
        string? environment = null;
        string? releaseName = null;
        string? reportPath = null;
        string? workflowPath = null;
        Severity? failOn = null;
        var json = false;
        var helpRequested = false;
        string? error = null;
        var index = 1;

        string? Take(string option)
        {
            if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
            {
                error = $"{option} requires a value.";
                return null;
            }

            index++;
            return args[index];
        }

        while (index < args.Length && error is null && !helpRequested)
        {
            var argument = args[index];

            if (IsHelpFlag(argument))
            {
                helpRequested = true;
                break;
            }

            switch (argument)
            {
                case "-f":
                case "--values":
                    if (Take(argument) is { } valuesFile)
                    {
                        valuesFiles.Add(valuesFile);
                    }

                    break;

                case "--profile":
                    profileId = Take(argument);
                    break;

                case "--environment":
                    environment = Take(argument);
                    break;

                case "--release":
                    releaseName = Take(argument);
                    break;

                case "--report":
                    reportPath = Take(argument);
                    break;

                case "--workflow":
                    workflowPath = Take(argument);
                    break;

                case "--fail-on":
                    if (Take(argument) is { } level)
                    {
                        failOn = ParseSeverity(level);

                        if (failOn is null)
                        {
                            error = $"--fail-on must be info, warning or critical, not '{level}'.";
                        }
                    }

                    break;

                case "--json":
                    json = true;
                    break;

                default:
                    if (argument.StartsWith('-'))
                    {
                        error = $"Unknown option '{argument}'.";
                    }
                    else if (chartPath is not null)
                    {
                        error = $"Unexpected argument '{argument}'.";
                    }
                    else
                    {
                        chartPath = argument;
                    }

                    break;
            }

            index++;
        }

        if (helpRequested)
        {
            return ParseResult.Ok(Empty(CliCommand.Help));
        }

        if (error is not null)
        {
            return ParseResult.Fail(error);
        }

        if (command is CliCommand.Check && string.IsNullOrWhiteSpace(chartPath))
        {
            return ParseResult.Fail("check requires a chart path.");
        }

        return ParseResult.Ok(new CliOptions(
            command.Value,
            chartPath,
            valuesFiles,
            profileId,
            environment,
            releaseName,
            reportPath,
            workflowPath,
            failOn,
            json));
    }

    public static Severity? ParseSeverity(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "info" => Severity.Info,
        "warning" => Severity.Warning,
        "critical" => Severity.Critical,
        _ => null
    };

    private static bool IsHelpFlag(string argument)
        => argument is "-h" or "--help" or "/?";

    private static CliOptions Empty(CliCommand command)
        => new(command, null, [], null, null, null, null, null, null, false);
}
