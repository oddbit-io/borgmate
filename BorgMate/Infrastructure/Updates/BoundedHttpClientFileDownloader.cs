using System.Collections.Generic;
using System.Net.Http;
using Velopack.Sources;

namespace BorgMate.Infrastructure.Updates;

/// <summary>
///     Velopack downloader that stops buffering a response once it exceeds
///     <see cref="MaxBufferedResponseBytes" />, so a hostile update server cannot exhaust memory with
///     the manifest, its signature or the feed. Packages stream to disk and are checked by hash instead.
/// </summary>
public class BoundedHttpClientFileDownloader : HttpClientFileDownloader
{
    /// <summary>Largest manifest, signature or feed the update check reads into memory.</summary>
    public const long MaxBufferedResponseBytes = 16 * 1024 * 1024;

    protected override HttpClient CreateHttpClient(IDictionary<string, string>? headers, double timeout)
    {
        var client = base.CreateHttpClient(headers, timeout);
        client.MaxResponseContentBufferSize = MaxBufferedResponseBytes;
        return client;
    }
}
