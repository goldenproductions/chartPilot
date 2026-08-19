using ChartPilot.Cli.Commands;

using var cancellation = new CancellationTokenSource();

Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

return await CliRunner.RunAsync(args, Console.Out, Console.Error, cancellation.Token).ConfigureAwait(false);
