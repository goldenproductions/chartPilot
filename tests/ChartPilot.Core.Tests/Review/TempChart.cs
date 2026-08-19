namespace ChartPilot.Core.Tests.Review;

/// <summary>
/// A throwaway chart directory. The pipeline resolves values files from disk, so the tests need a
/// real directory even though nothing renders it.
/// </summary>
internal sealed class TempChart : IDisposable
{
    public TempChart()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "chartpilot-tests",
            Guid.NewGuid().ToString("n"));

        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string Write(string fileName, string content)
    {
        var full = System.IO.Path.Combine(Path, fileName);
        File.WriteAllText(full, content);
        return full;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a test over.
        }
    }
}
