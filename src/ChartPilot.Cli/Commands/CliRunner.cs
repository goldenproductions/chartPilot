using ChartPilot.Cli.CommandLine;
using ChartPilot.Core.Checks;
using ChartPilot.Core.Charts;
using ChartPilot.Core.Helm;
using ChartPilot.Core.Manifests;
using ChartPilot.Core.Profiles;
using ChartPilot.Core.Reporting;
using ChartPilot.Core.Review;
using ChartPilot.Core.Scoring;
using ChartPilot.Core.Values;
using ChartPilot.Helm;
using Microsoft.Extensions.DependencyInjection;

namespace ChartPilot.Cli.Commands;

/// <summary>Parses the command line, builds the container and dispatches to a command.</summary>
internal static class CliRunner
{
    public static async Task<int> RunAsync(string[] args, TextWriter output, TextWriter error, CancellationToken ct)
    {
        var parsed = ArgumentParser.Parse(args);

        if (parsed.Error is { } message)
        {
            error.WriteLine(message);
            error.WriteLine();
            error.WriteLine(ArgumentParser.Usage);
            return ExitCodes.ExecutionError;
        }

        var options = parsed.Options!;

        if (options.Command is CliCommand.Help)
        {
            output.WriteLine(ArgumentParser.Usage);
            return ExitCodes.Clean;
        }

        try
        {
            return options.Command switch
            {
                CliCommand.Check => await CheckCommand.RunAsync(options, output, error, ct).ConfigureAwait(false),
                CliCommand.Profiles => CatalogCommands.Profiles(BuildProvider(Directory.GetCurrentDirectory()), output),
                CliCommand.Checks => CatalogCommands.Checks(BuildProvider(Directory.GetCurrentDirectory()), output),
                _ => ExitCodes.ExecutionError
            };
        }
        catch (HelmNotAvailableException ex)
        {
            error.WriteLine($"helm is not available: {ex.Message}");
            error.WriteLine("Install it with: winget install Helm.Helm");
            return ExitCodes.ExecutionError;
        }
        catch (ReviewException ex)
        {
            error.WriteLine(ex.Message);

            if (!string.IsNullOrWhiteSpace(ex.HelmStdErr))
            {
                error.WriteLine(ex.HelmStdErr.TrimEnd());
            }

            return ExitCodes.ExecutionError;
        }
        catch (OperationCanceledException)
        {
            error.WriteLine("Cancelled.");
            return ExitCodes.ExecutionError;
        }
        catch (ManifestParseException ex)
        {
            error.WriteLine($"The rendered manifests could not be parsed: {ex.Message}");
            return ExitCodes.ExecutionError;
        }
        catch (Exception ex) when (IsUserInputFailure(ex))
        {
            // A refused path, an unreadable report target or a malformed chart is the user's input,
            // not a defect: architecture.md section 9 says every such failure is exit code 2 with a
            // message, never an unhandled exception and a stack trace.
            error.WriteLine(Describe(ex));
            return ExitCodes.ExecutionError;
        }
    }

    /// <summary>
    /// ArgumentException appends "(Parameter 'x')" to its message, which is noise on a command line:
    /// the user did not pass a parameter called valuesFiles, they passed --values.
    /// </summary>
    private static string Describe(Exception exception)
    {
        var message = exception.Message;
        var marker = message.IndexOf(" (Parameter '", StringComparison.Ordinal);

        return marker > 0 ? message[..marker] : message;
    }

    /// <summary>
    /// The exception types that mean "ChartPilot was asked to do something it cannot do", as opposed
    /// to a defect in ChartPilot itself. Anything outside this set still propagates, because a
    /// NullReferenceException swallowed into exit code 2 is a bug that never gets reported.
    /// </summary>
    private static bool IsUserInputFailure(Exception exception) => exception
        is ArgumentException          // a values file resolving outside the allowlist root
        or ChartLoadException         // a malformed Chart.yaml
        or ValuesParseException       // a malformed values file
        or IOException                // --report pointing at an unwritable path
        or UnauthorizedAccessException
        or NotSupportedException;

    /// <summary>
    /// The allowlist root is the chart's parent directory: the CLI renders exactly the chart it was
    /// pointed at, and a template that tries to escape that root is refused by the Helm client.
    /// </summary>
    public static ServiceProvider BuildProvider(string allowlistRoot)
    {
        var services = new ServiceCollection();

        services.AddLogging();

        services
            .AddChartPilotCharts()
            .AddChartPilotValues()
            .AddChartPilotManifests()
            .AddChartPilotChecks()
            .AddChartPilotProfiles()
            .AddChartPilotScoring()
            .AddChartPilotReview()
            .AddChartPilotReporting()
            .AddChartPilotHelm(options => options.AllowlistRoot = allowlistRoot);

        return services.BuildServiceProvider();
    }
}
