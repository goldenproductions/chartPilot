using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ChartPilot.Helm;

/// <summary>
/// Runs a child process with redirected output. stdout and stderr are drained concurrently
/// (reading them sequentially deadlocks once a pipe buffer fills), the wall-clock timeout kills
/// the process tree, and accumulation stops at the output cap while the pipes keep draining.
/// </summary>
public sealed class ProcessRunner : IProcessRunner
{
    private static readonly TimeSpan KillGracePeriod = TimeSpan.FromSeconds(5);

    private readonly ILogger<ProcessRunner> _logger;

    public ProcessRunner(ILogger<ProcessRunner>? logger = null)
    {
        _logger = logger ?? NullLogger<ProcessRunner>.Instance;
    }

    /// <inheritdoc />
    public async Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var startInfo = new ProcessStartInfo
        {
            FileName = request.FileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (var argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (!string.IsNullOrWhiteSpace(request.WorkingDirectory))
        {
            startInfo.WorkingDirectory = request.WorkingDirectory;
        }

        foreach (var (key, value) in request.EnvironmentOverrides)
        {
            if (value is null)
            {
                startInfo.Environment.Remove(key);
            }
            else
            {
                startInfo.Environment[key] = value;
            }
        }

        using var process = new Process { StartInfo = startInfo };

        var stopwatch = Stopwatch.StartNew();
        process.Start();

        // The child never reads stdin; closing it prevents a template that prompts from hanging us.
        try
        {
            process.StandardInput.Close();
        }
        catch (IOException)
        {
            // The process may already have exited.
        }

        var budget = Math.Max(0, request.MaxOutputBytes);
        var stdOutTask = DrainAsync(process.StandardOutput, budget);
        var stdErrTask = DrainAsync(process.StandardError, budget);

        var timedOut = false;
        var canceled = false;

        using var timeoutSource = new CancellationTokenSource();
        if (request.Timeout > TimeSpan.Zero)
        {
            timeoutSource.CancelAfter(request.Timeout);
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);

        try
        {
            await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            canceled = cancellationToken.IsCancellationRequested;
            timedOut = !canceled;
            KillTree(process);

            try
            {
                await process.WaitForExitAsync(CancellationToken.None)
                    .WaitAsync(KillGracePeriod, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                _logger.LogWarning("Process {FileName} did not exit within the kill grace period.", request.FileName);
            }
        }

        var stdOut = await stdOutTask.ConfigureAwait(false);
        var stdErr = await stdErrTask.ConfigureAwait(false);
        stopwatch.Stop();

        if (canceled)
        {
            throw new OperationCanceledException(cancellationToken);
        }

        var exitCode = -1;
        if (!timedOut)
        {
            try
            {
                exitCode = process.ExitCode;
            }
            catch (InvalidOperationException)
            {
                exitCode = -1;
            }
        }

        return new ProcessResult(
            exitCode,
            stdOut.Text,
            stdErr.Text,
            stopwatch.Elapsed,
            timedOut,
            stdOut.Truncated || stdErr.Truncated);
    }

    private static void KillTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // Already exited.
        }
        catch (NotSupportedException)
        {
            // Platform without process-tree support; nothing further to do.
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // The process died between the check and the kill.
        }
    }

    private static async Task<CapturedOutput> DrainAsync(StreamReader reader, int maxBytes)
    {
        var builder = new StringBuilder();
        var buffer = new char[8192];
        var captured = 0;
        var truncated = false;

        while (true)
        {
            int read;
            try
            {
                read = await reader.ReadAsync(buffer.AsMemory(), CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException)
            {
                // The pipe closed under us (killed process). Whatever was captured stands.
                break;
            }

            if (read == 0)
            {
                break;
            }

            if (captured >= maxBytes)
            {
                truncated = true;
                continue;
            }

            var chunk = new string(buffer, 0, read);
            var chunkBytes = Encoding.UTF8.GetByteCount(chunk);

            if (captured + chunkBytes <= maxBytes)
            {
                builder.Append(chunk);
                captured += chunkBytes;
                continue;
            }

            var remaining = maxBytes - captured;
            var take = 0;
            var used = 0;
            while (take < chunk.Length)
            {
                var next = Encoding.UTF8.GetByteCount(chunk.AsSpan(take, 1));
                if (used + next > remaining)
                {
                    break;
                }

                used += next;
                take++;
            }

            builder.Append(chunk, 0, take);
            captured += used;
            truncated = true;
        }

        return new CapturedOutput(builder.ToString(), truncated);
    }

    private readonly record struct CapturedOutput(string Text, bool Truncated);
}
