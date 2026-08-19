using System.Text.Json;
using ChartPilot.Cli.CommandLine;
using ChartPilot.Cli.Output;
using ChartPilot.Core.Checks;
using ChartPilot.Core.Reporting;
using ChartPilot.Core.Review;
using Microsoft.Extensions.DependencyInjection;

namespace ChartPilot.Cli.Commands;

/// <summary><c>chartpilot check</c> — the CI face of the same pipeline the GUI runs.</summary>
internal static class CheckCommand
{
    public static async Task<int> RunAsync(
        CliOptions options,
        TextWriter output,
        TextWriter error,
        CancellationToken ct)
    {
        var chartPath = Path.GetFullPath(options.ChartPath!);

        if (!Directory.Exists(chartPath))
        {
            error.WriteLine($"Chart directory not found: {options.ChartPath}");
            return ExitCodes.ExecutionError;
        }

        var allowlistRoot = Directory.GetParent(chartPath)?.FullName ?? chartPath;

        await using var provider = CliRunner.BuildProvider(allowlistRoot);

        var pipeline = provider.GetRequiredService<IReviewPipeline>();

        var request = new ReviewRequest(
            chartPath,
            options.ReleaseName ?? Path.GetFileName(chartPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
            options.ValuesFiles,
            DraftValuesYaml: null,
            options.ProfileId ?? string.Empty,
            options.Environment ?? "default");

        var result = await pipeline.ReviewAsync(request, ct).ConfigureAwait(false);

        if (options.ReportPath is { } reportPath)
        {
            var writer = provider.GetRequiredService<IReportWriter>();
            WriteFile(reportPath, writer.Write(result));
            output.WriteLine($"Report written to {reportPath}");
        }

        if (options.WorkflowPath is { } workflowPath)
        {
            var generator = provider.GetRequiredService<IWorkflowGenerator>();

            var environments = result.Chart.ValuesFiles
                .Where(v => !string.IsNullOrEmpty(v.EnvironmentName))
                .Select(v => v.EnvironmentName!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var workflow = generator.Generate(new WorkflowOptions(
                "./" + Path.GetFileName(chartPath),
                result.Chart.Name,
                environments,
                result.ProfileId,
                (options.FailOn ?? Severity.Critical).ToString().ToLowerInvariant(),
                "default"));

            WriteFile(workflowPath, workflow);
            output.WriteLine($"Workflow written to {workflowPath}");
        }

        if (options.Json)
        {
            output.WriteLine(JsonSerializer.Serialize(JsonReviewMapper.From(result), JsonReviewMapper.Options));
        }
        else
        {
            ConsoleReport.Write(output, result, reportWritten: options.ReportPath is not null, explain: options.Explain);
        }

        return Gate(result, options.FailOn);
    }

    /// <summary>
    /// The gate is on findings, not on the score: a score is a conversation starter, a critical
    /// finding is a blocker.
    /// </summary>
    private static int Gate(ReviewResult result, Severity? failOn)
    {
        if (failOn is not { } threshold)
        {
            return ExitCodes.Clean;
        }

        foreach (var finding in result.Findings)
        {
            if (finding.Severity >= threshold)
            {
                return ExitCodes.GateFailed;
            }
        }

        return ExitCodes.Clean;
    }

    private static void WriteFile(string path, string content)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, content);
    }
}
