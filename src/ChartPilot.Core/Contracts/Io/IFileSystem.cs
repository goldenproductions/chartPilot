namespace ChartPilot.Core.Io;

/// <summary>
/// The only way ChartPilot.Core is allowed to touch a disk. Core owns the pipeline's decisions —
/// which values files layer in which order, what "the chart directory" means — and those decisions
/// are testable exactly to the extent that the reads behind them are substitutable
/// (architecture.md section 3: "no disk beyond abstractions").
/// </summary>
public interface IFileSystem
{
    bool FileExists(string path);

    bool DirectoryExists(string path);

    /// <summary>Reads a whole text file. Throws <see cref="IOException"/> the way <c>File.ReadAllText</c> does.</summary>
    string ReadAllText(string path);

    /// <summary>The rooted, normalised form of a path, without touching the disk.</summary>
    string GetFullPath(string path);
}
