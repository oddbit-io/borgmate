using System.Net;
using Velopack.Sources;

namespace BorgMate.Tests.Infrastructure;

public class FakeFileDownloader : IFileDownloader
{
    public Dictionary<string, byte[]> Files { get; } = new();

    public Task<byte[]> DownloadBytes(string url, IDictionary<string, string>? headers = null, double timeout = 30)
    {
        return Task.FromResult(Get(url));
    }

    public async Task DownloadFile(string url, string targetFile, Action<int> progress,
        IDictionary<string, string>? headers = null, double timeout = 30, CancellationToken cancelToken = default)
    {
        await File.WriteAllBytesAsync(targetFile, Get(url), cancelToken);
        progress(100);
    }

    public Task<string> DownloadString(string url, IDictionary<string, string>? headers = null, double timeout = 30)
    {
        throw new NotSupportedException("Update sources must read raw bytes.");
    }

    private byte[] Get(string url)
    {
        return Files.TryGetValue(url, out var bytes)
            ? bytes
            : throw new HttpRequestException($"Not found: {url}", null, HttpStatusCode.NotFound);
    }
}
