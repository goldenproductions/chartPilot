using ChartPilot.Helm;

namespace ChartPilot.Helm.Tests;

public sealed class HelmLocatorTests : IDisposable
{
    private readonly string _root;

    public HelmLocatorTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "chartpilot-locator", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // Leftover temp directories are not a test failure.
        }
    }

    /// <summary>A fake machine that can only see files underneath this test's temp root.</summary>
    private FakeHelmEnvironment Env(string? home = null) =>
        new(home) { RestrictToRoot = _root };

    private static string ExecutableName => OperatingSystem.IsWindows() ? "helm.exe" : "helm";

    private string CreateBinary(params string[] segments)
    {
        var path = Path.Combine([_root, .. segments]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "fake helm");
        return path;
    }

    private static FakeProcessRunner VersionRunner(string output = "v4.2.4+gabc1234\n", int exitCode = 0) =>
        new()
        {
            Result = new ProcessResult(exitCode, output, string.Empty, TimeSpan.FromMilliseconds(4), false, false)
        };

    private static HelmLocator Locator(FakeProcessRunner runner, ChartPilotHelmOptions options, FakeHelmEnvironment environment) =>
        new(runner, MicrosoftOptions.Create(options), null, environment);

    [Fact]
    public async Task Configured_path_wins()
    {
        var configured = CreateBinary("configured", ExecutableName);
        CreateBinary("onpath", ExecutableName);

        var environment = Env().Set("PATH", Path.Combine(_root, "onpath"));
        var locator = Locator(VersionRunner(), new ChartPilotHelmOptions { HelmPath = configured }, environment);

        var executable = await locator.ResolveAsync();

        Assert.True(executable.IsAvailable);
        Assert.Equal(configured, executable.Path);
        Assert.Equal(HelmLocator.SourceConfiguration, executable.ResolutionSource);
        Assert.Equal("4.2.4", executable.Version);
    }

    [Fact]
    public async Task Configured_directory_is_probed_for_the_binary()
    {
        var configuredDirectory = Path.Combine(_root, "configured");
        var binary = CreateBinary("configured", ExecutableName);

        var locator = Locator(VersionRunner(), new ChartPilotHelmOptions { HelmPath = configuredDirectory }, Env());

        var executable = await locator.ResolveAsync();

        Assert.True(executable.IsAvailable);
        Assert.Equal(binary, executable.Path);
        Assert.Equal(HelmLocator.SourceConfiguration, executable.ResolutionSource);
    }

    [Fact]
    public async Task A_missing_configured_path_falls_through_to_PATH()
    {
        var onPath = CreateBinary("onpath", ExecutableName);

        var environment = Env().Set(
            "PATH",
            string.Join(Path.PathSeparator, Path.Combine(_root, "empty"), Path.Combine(_root, "onpath")));

        var options = new ChartPilotHelmOptions { HelmPath = Path.Combine(_root, "nope", ExecutableName) };
        var locator = Locator(VersionRunner(), options, environment);

        var executable = await locator.ResolveAsync();

        Assert.True(executable.IsAvailable);
        Assert.Equal(onPath, executable.Path);
        Assert.Equal(HelmLocator.SourcePath, executable.ResolutionSource);
    }

    [Fact]
    public async Task PATH_entries_are_walked_in_order()
    {
        var first = CreateBinary("first", ExecutableName);
        CreateBinary("second", ExecutableName);

        var environment = Env().Set(
            "PATH",
            string.Join(Path.PathSeparator, Path.Combine(_root, "first"), Path.Combine(_root, "second")));

        var locator = Locator(VersionRunner(), new ChartPilotHelmOptions(), environment);

        var executable = await locator.ResolveAsync();

        Assert.Equal(first, executable.Path);
    }

    [Fact]
    public async Task The_winget_package_directory_is_globbed()
    {
        var binary = CreateBinary(
            "localappdata", "Microsoft", "WinGet", "Packages",
            "Helm.Helm_Microsoft.Winget.Source_8wekyb3d8bbwe", "windows-amd64", "helm.exe");

        var environment = Env().Set("LOCALAPPDATA", Path.Combine(_root, "localappdata"));
        var locator = Locator(VersionRunner(), new ChartPilotHelmOptions(), environment);

        var executable = await locator.ResolveAsync();

        Assert.True(executable.IsAvailable);
        Assert.Equal(binary, executable.Path);
        Assert.Equal(HelmLocator.SourceWellKnown, executable.ResolutionSource);
    }

    [Fact]
    public async Task The_chocolatey_shim_is_a_well_known_location()
    {
        var binary = CreateBinary("programdata", "chocolatey", "bin", "helm.exe");

        var environment = Env().Set("ProgramData", Path.Combine(_root, "programdata"));
        var locator = Locator(VersionRunner(), new ChartPilotHelmOptions(), environment);

        var executable = await locator.ResolveAsync();

        Assert.Equal(binary, executable.Path);
        Assert.Equal(HelmLocator.SourceWellKnown, executable.ResolutionSource);
    }

    [Fact]
    public async Task The_program_files_location_is_probed()
    {
        var binary = CreateBinary("programfiles", "helm", "helm.exe");

        var environment = Env().Set("ProgramFiles", Path.Combine(_root, "programfiles"));
        var locator = Locator(VersionRunner(), new ChartPilotHelmOptions(), environment);

        var executable = await locator.ResolveAsync();

        Assert.Equal(binary, executable.Path);
    }

    [Fact]
    public async Task The_home_local_bin_location_is_probed()
    {
        var binary = CreateBinary("home", ".local", "bin", "helm");

        var environment = Env(Path.Combine(_root, "home"));
        var locator = Locator(VersionRunner(), new ChartPilotHelmOptions(), environment);

        var executable = await locator.ResolveAsync();

        Assert.Equal(binary, executable.Path);
        Assert.Equal(HelmLocator.SourceWellKnown, executable.ResolutionSource);
    }

    [Fact]
    public async Task Not_found_produces_an_actionable_message()
    {
        var runner = VersionRunner();
        var locator = Locator(runner, new ChartPilotHelmOptions(), Env());

        var executable = await locator.ResolveAsync();

        Assert.False(executable.IsAvailable);
        Assert.Null(executable.Path);
        Assert.Equal(HelmLocator.SourceNotFound, executable.ResolutionSource);
        Assert.NotNull(executable.Error);
        Assert.Contains("winget install Helm.Helm", executable.Error!, StringComparison.Ordinal);
        Assert.Equal(0, runner.CallCount);
    }

    [Fact]
    public async Task A_binary_that_cannot_report_a_version_is_not_available()
    {
        var configured = CreateBinary("configured", ExecutableName);
        var runner = VersionRunner("", exitCode: 1);
        runner.Result = new ProcessResult(1, string.Empty, "not a helm binary", TimeSpan.FromMilliseconds(4), false, false);

        var locator = Locator(runner, new ChartPilotHelmOptions { HelmPath = configured }, Env());

        var executable = await locator.ResolveAsync();

        Assert.False(executable.IsAvailable);
        Assert.Equal(configured, executable.Path);
        Assert.Contains("not a helm binary", executable.Error!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_successful_resolution_is_cached_for_the_process()
    {
        var configured = CreateBinary("configured", ExecutableName);
        var runner = VersionRunner();
        var locator = Locator(runner, new ChartPilotHelmOptions { HelmPath = configured }, Env());

        var first = await locator.ResolveAsync();
        var second = await locator.ResolveAsync();

        Assert.Same(first, second);
        Assert.Equal(1, runner.CallCount);

        locator.Invalidate();
        await locator.ResolveAsync();
        Assert.Equal(2, runner.CallCount);
    }

    [Fact]
    public async Task A_failed_resolution_is_not_cached()
    {
        var runner = VersionRunner();
        var options = new ChartPilotHelmOptions();
        var environment = Env();
        var locator = Locator(runner, options, environment);

        var missing = await locator.ResolveAsync();
        Assert.False(missing.IsAvailable);

        // helm gets installed while ChartPilot is running.
        var installed = CreateBinary("late", ExecutableName);
        environment.Set("PATH", Path.Combine(_root, "late"));

        var found = await locator.ResolveAsync();

        Assert.True(found.IsAvailable);
        Assert.Equal(installed, found.Path);
    }

    [Fact]
    public async Task The_version_probe_uses_the_short_flag()
    {
        var configured = CreateBinary("configured", ExecutableName);
        var runner = VersionRunner();
        var locator = Locator(runner, new ChartPilotHelmOptions { HelmPath = configured }, Env());

        await locator.ResolveAsync();

        Assert.Equal(configured, runner.LastRequest.FileName);
        Assert.Equal(new[] { "version", "--short" }, runner.LastRequest.Arguments);
    }

    [Theory]
    [InlineData("v4.2.4+gabc1234", "4.2.4")]
    [InlineData("v3.16.2+g13654a5", "3.16.2")]
    [InlineData("4.2.4", "4.2.4")]
    [InlineData("v4.2.4-rc.1+gdeadbee", "4.2.4-rc.1")]
    [InlineData("", null)]
    [InlineData("something else entirely", null)]
    public void ParseVersion_reads_the_semantic_version(string output, string? expected)
    {
        Assert.Equal(expected, HelmLocator.ParseVersion(output));
    }
}
