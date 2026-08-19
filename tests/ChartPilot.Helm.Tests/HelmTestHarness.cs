using ChartPilot.Helm;
using Microsoft.Extensions.Options;

namespace ChartPilot.Helm.Tests;

/// <summary>
/// A disposable temp directory holding a fake helm binary and a chart directory, plus a
/// <see cref="HelmClient"/> wired to a <see cref="FakeProcessRunner"/>.
/// </summary>
internal sealed class HelmTestHarness : IDisposable
{
    public const string VersionOutput = "v4.2.4+gabc1234\n";

    private HelmTestHarness(string root)
    {
        Root = root;
        ChartPath = Path.Combine(root, "chart");
        Directory.CreateDirectory(ChartPath);
        File.WriteAllText(Path.Combine(ChartPath, "Chart.yaml"), "apiVersion: v2\nname: chart\nversion: 0.1.0\n");

        var binDirectory = Path.Combine(root, "bin");
        Directory.CreateDirectory(binDirectory);
        HelmExecutablePath = Path.Combine(binDirectory, OperatingSystem.IsWindows() ? "helm.exe" : "helm");
        File.WriteAllText(HelmExecutablePath, "fake");

        Runner = new FakeProcessRunner
        {
            Handler = request => IsVersionCall(request)
                ? new ProcessResult(0, VersionOutput, string.Empty, TimeSpan.FromMilliseconds(3), false, false)
                : HelmResult
        };

        Options = new ChartPilotHelmOptions
        {
            HelmPath = HelmExecutablePath,
            AllowlistRoot = root
        };

        Locator = new HelmLocator(Runner, MicrosoftOptions.Create(Options), null, new FakeHelmEnvironment { RestrictToRoot = root });
        Client = new HelmClient(Runner, Locator, MicrosoftOptions.Create(Options));
    }

    public static HelmTestHarness Create([System.Runtime.CompilerServices.CallerMemberName] string name = "test")
    {
        var root = Path.Combine(Path.GetTempPath(), "chartpilot-tests", $"{name}-{Guid.NewGuid():n}");
        Directory.CreateDirectory(root);
        return new HelmTestHarness(root);
    }

    public string Root { get; }

    public string ChartPath { get; }

    public string HelmExecutablePath { get; }

    public FakeProcessRunner Runner { get; }

    public HelmLocator Locator { get; }

    public HelmClient Client { get; }

    public ChartPilotHelmOptions Options { get; }

    /// <summary>The result returned for every non-version helm invocation.</summary>
    public ProcessResult HelmResult { get; set; } =
        new(0, "# Source: chart/templates/deployment.yaml\nkind: Deployment\n", string.Empty, TimeSpan.FromMilliseconds(40), false, false);

    /// <summary>Extra behaviour for the helm invocation itself; the version call is handled for you.</summary>
    public void OnHelmCall(Func<ProcessRequest, ProcessResult> handler)
    {
        Runner.Handler = request => IsVersionCall(request)
            ? new ProcessResult(0, VersionOutput, string.Empty, TimeSpan.FromMilliseconds(3), false, false)
            : handler(request);
    }

    /// <summary>The arguments of the last non-version helm invocation.</summary>
    public IReadOnlyList<string> HelmArguments =>
        Runner.Requests.Where(static r => !IsVersionCall(r)).Select(static r => r.Arguments).LastOrDefault()
        ?? throw new InvalidOperationException("helm was never invoked.");

    /// <summary>The last non-version helm invocation.</summary>
    public ProcessRequest HelmRequest =>
        Runner.Requests.LastOrDefault(static r => !IsVersionCall(r))
        ?? throw new InvalidOperationException("helm was never invoked.");

    public string WriteValuesFile(string relativePath, string content = "replicaCount: 1\n")
    {
        var full = Path.Combine(Root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return full;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
        catch (IOException)
        {
            // A test temp directory that survives is not a test failure.
        }
    }

    private static bool IsVersionCall(ProcessRequest request) =>
        request.Arguments.Count > 0 && request.Arguments[0] == "version";
}

/// <summary>Alias so the harness can use Options.Create without colliding with the options type name.</summary>
internal static class MicrosoftOptions
{
    public static IOptions<T> Create<T>(T value) where T : class => Microsoft.Extensions.Options.Options.Create(value);
}
