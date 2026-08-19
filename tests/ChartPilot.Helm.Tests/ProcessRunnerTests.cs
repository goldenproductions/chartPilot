using ChartPilot.Helm;

namespace ChartPilot.Helm.Tests;

/// <summary>
/// Exercises the real process runner against a shell built into the operating system.
/// No helm and no network: these tests cover the plumbing (concurrent draining, timeout kill,
/// output cap, cancellation) that a fake runner cannot cover.
/// </summary>
public sealed class ProcessRunnerTests
{
    private static ProcessRequest Shell(string command, TimeSpan? timeout = null, int maxOutputBytes = 1024 * 1024)
    {
        var (fileName, arguments) = OperatingSystem.IsWindows()
            ? ("cmd.exe", new[] { "/d", "/c", command })
            : ("/bin/sh", new[] { "-c", command });

        return new ProcessRequest(fileName, arguments, null, timeout ?? TimeSpan.FromSeconds(30), maxOutputBytes);
    }

    [Fact]
    public async Task RunAsync_captures_stdout_and_the_exit_code()
    {
        var runner = new ProcessRunner();

        var result = await runner.RunAsync(Shell("echo chartpilot"), CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("chartpilot", result.StdOut, StringComparison.Ordinal);
        Assert.False(result.TimedOut);
        Assert.False(result.OutputTruncated);
    }

    [Fact]
    public async Task RunAsync_captures_stderr_and_a_non_zero_exit_code()
    {
        var runner = new ProcessRunner();

        var command = OperatingSystem.IsWindows()
            ? "echo boom 1>&2 & exit /b 3"
            : "echo boom 1>&2; exit 3";

        var result = await runner.RunAsync(Shell(command), CancellationToken.None);

        Assert.Equal(3, result.ExitCode);
        Assert.Contains("boom", result.StdErr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_kills_the_process_and_reports_a_timeout()
    {
        var runner = new ProcessRunner();

        var command = OperatingSystem.IsWindows()
            ? "ping -n 20 127.0.0.1 > nul"
            : "sleep 20";

        var result = await runner.RunAsync(Shell(command, TimeSpan.FromMilliseconds(400)), CancellationToken.None);

        Assert.True(result.TimedOut);
        Assert.True(result.Duration < TimeSpan.FromSeconds(15));
    }

    [Fact]
    public async Task RunAsync_stops_accumulating_output_past_the_cap()
    {
        var runner = new ProcessRunner();

        var command = OperatingSystem.IsWindows()
            ? "for /l %i in (1,1,200) do @echo 0123456789012345678901234567890123456789"
            : "for i in $(seq 1 200); do echo 0123456789012345678901234567890123456789; done";

        var result = await runner.RunAsync(Shell(command, maxOutputBytes: 200), CancellationToken.None);

        Assert.True(result.OutputTruncated);
        Assert.True(result.StdOut.Length <= 200);
    }

    [Fact]
    public async Task RunAsync_throws_when_the_caller_cancels()
    {
        var runner = new ProcessRunner();
        using var cts = new CancellationTokenSource();

        var command = OperatingSystem.IsWindows()
            ? "ping -n 20 127.0.0.1 > nul"
            : "sleep 20";

        var task = runner.RunAsync(Shell(command), cts.Token);
        cts.CancelAfter(TimeSpan.FromMilliseconds(300));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }

    [Fact]
    public async Task RunAsync_applies_environment_overrides()
    {
        var runner = new ProcessRunner();
        Environment.SetEnvironmentVariable("CHARTPILOT_TEST_VAR", "present");

        try
        {
            var command = OperatingSystem.IsWindows()
                ? "echo [%CHARTPILOT_TEST_VAR%]"
                : "echo [${CHARTPILOT_TEST_VAR:-}]";

            var request = Shell(command) with
            {
                EnvironmentOverrides = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["CHARTPILOT_TEST_VAR"] = null
                }
            };

            var result = await runner.RunAsync(request, CancellationToken.None);

            Assert.DoesNotContain("present", result.StdOut, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CHARTPILOT_TEST_VAR", null);
        }
    }
}
