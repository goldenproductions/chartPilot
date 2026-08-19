namespace ChartPilot.Helm;

/// <summary>
/// Options for locating and running the helm binary. Bound from the <c>ChartPilot</c>
/// configuration section by the API host, or configured in code by the CLI.
/// </summary>
public sealed class ChartPilotHelmOptions
{
    /// <summary>The configuration section these options are bound from.</summary>
    public const string SectionName = "ChartPilot";

    /// <summary>
    /// Explicit path to the helm binary. Takes priority over PATH and the well-known locations.
    /// May point at the binary itself or at the directory containing it.
    /// </summary>
    public string? HelmPath { get; set; }

    /// <summary>
    /// Charts and values files must resolve underneath this directory. Defaults to the
    /// current working directory. This is the traversal guard for a tool that renders
    /// arbitrary Go templates.
    /// </summary>
    public string? AllowlistRoot { get; set; }

    /// <summary>Wall-clock limit for a single <c>helm template</c> run.</summary>
    public TimeSpan TemplateTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Wall-clock limit for a single <c>helm lint</c> run.</summary>
    public TimeSpan LintTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Maximum number of bytes captured from each of stdout and stderr. Defaults to 8 MiB.</summary>
    public int MaxOutputBytes { get; set; } = 8 * 1024 * 1024;

    /// <summary>Release name used when a request does not carry one.</summary>
    public string DefaultReleaseName { get; set; } = "chartpilot";

    /// <summary>The effective allowlist root: <see cref="AllowlistRoot"/> or the current directory.</summary>
    public string ResolveAllowlistRoot() =>
        string.IsNullOrWhiteSpace(AllowlistRoot)
            ? PathGuard.Normalize(Directory.GetCurrentDirectory())
            : PathGuard.Normalize(AllowlistRoot);

    /// <summary>The effective release name for a request, falling back to <see cref="DefaultReleaseName"/>.</summary>
    public string ResolveReleaseName(string? requested) =>
        string.IsNullOrWhiteSpace(requested)
            ? (string.IsNullOrWhiteSpace(DefaultReleaseName) ? "chartpilot" : DefaultReleaseName.Trim())
            : requested.Trim();
}
