namespace BorgMate.Tests.Infrastructure;

/// <summary>A temporary directory deleted on dispose; <see cref="Path" /> uses forward slashes.</summary>
internal class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        var directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bm-sec-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        Path = directory.Replace('\\', '/');
    }

    public string Path { get; }

    public void Dispose()
    {
        Directory.Delete(Path, true);
    }
}
