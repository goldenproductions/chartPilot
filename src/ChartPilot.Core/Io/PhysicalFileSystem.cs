namespace ChartPilot.Core.Io;

/// <summary>The real filesystem. The only implementation that ships; tests substitute their own.</summary>
public sealed class PhysicalFileSystem : IFileSystem
{
    public bool FileExists(string path) => !string.IsNullOrWhiteSpace(path) && File.Exists(path);

    public bool DirectoryExists(string path) => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path);

    public string ReadAllText(string path) => File.ReadAllText(path);

    public string GetFullPath(string path) => Path.GetFullPath(path);
}
