using System;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Velopack;
using Velopack.Logging;
using Velopack.Sources;

namespace BorgMate.Infrastructure.Updates;

/// <summary>
///     Velopack update source that hands out a platform feed only after its signed manifest
///     proves the feed bytes were released for this platform and channel. Velopack then checks
///     every downloaded package against the SHA-256 in that feed.
/// </summary>
public class SignedReleaseFeedSource : IUpdateSource
{
    private const string ManifestFileName = "release-manifest.json";
    private const string SignatureFileName = ManifestFileName + ".asc";
    private const int SupportedManifestFormat = 1;
    private const int MaxManifestBytes = 16 * 1024;

    private readonly Uri _platformUri;
    private readonly string _platform;
    private readonly IFileDownloader _downloader;
    private readonly ReleaseSignatureVerifier _verifier;
    private readonly SimpleWebSource _packageSource;

    /// <param name="platformUri">Release directory of this platform on the update server.</param>
    /// <param name="platform">Directory name the manifest must name, for example <c>win-arm64</c>.</param>
    public SignedReleaseFeedSource(Uri platformUri, string platform, IFileDownloader downloader,
        ReleaseSignatureVerifier verifier)
    {
        _platformUri = platformUri.AbsoluteUri.EndsWith('/') ? platformUri : new Uri(platformUri.AbsoluteUri + "/");
        _platform = platform;
        _downloader = downloader;
        _verifier = verifier;
        _packageSource = new SimpleWebSource(_platformUri, downloader);
    }

    public async Task<VelopackAssetFeed> GetReleaseFeed(IVelopackLogger logger, string? appId, string channel,
        Guid? stagingId = null, VelopackAsset? latestLocalRelease = null)
    {
        var manifestBytes = await DownloadSignedFileAsync(ManifestFileName).ConfigureAwait(false);
        var signature = await DownloadSignedFileAsync(SignatureFileName).ConfigureAwait(false);
        if (manifestBytes.Length > MaxManifestBytes)
            throw new ReleaseVerificationException(ReleaseVerificationFailure.InvalidManifest,
                $"The release manifest is larger than {MaxManifestBytes} bytes.");

        _verifier.Verify(manifestBytes, signature);
        var manifest = ParseManifest(manifestBytes);
        if (manifest.FormatVersion != SupportedManifestFormat || manifest.AppId != appId
                                                              || manifest.Platform != _platform
                                                              || manifest.Channel != channel)
            throw new ReleaseVerificationException(ReleaseVerificationFailure.ManifestMismatch,
                $"The release manifest is for {manifest.AppId} {manifest.Platform}/{manifest.Channel} " +
                $"format {manifest.FormatVersion}, expected {appId} {_platform}/{channel} format {SupportedManifestFormat}.");

        var feedFileName = $"releases.{channel}.json";
        logger.Info($"Downloading verified release feed '{feedFileName}'.");
        var feedBytes = await _downloader.DownloadBytes(FileUrl(feedFileName)).ConfigureAwait(false);
        if (!Convert.ToHexString(SHA256.HashData(feedBytes)).Equals(manifest.FeedSha256, StringComparison.OrdinalIgnoreCase))
            throw new ReleaseVerificationException(ReleaseVerificationFailure.FeedHashMismatch,
                $"The feed '{feedFileName}' does not match the signed release manifest.");

        return VelopackAssetFeed.FromJson(DecodeUtf8(feedBytes));
    }

    public Task DownloadReleaseEntry(IVelopackLogger logger, VelopackAsset releaseEntry, string localFile,
        Action<int> progress, CancellationToken cancelToken)
    {
        return _packageSource.DownloadReleaseEntry(logger, releaseEntry, localFile, progress, cancelToken);
    }

    private async Task<byte[]> DownloadSignedFileAsync(string fileName)
    {
        try
        {
            return await _downloader.DownloadBytes(FileUrl(fileName)).ConfigureAwait(false);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            throw new ReleaseVerificationException(ReleaseVerificationFailure.SignatureMissing,
                $"The update server has no '{fileName}'.", ex);
        }
    }

    private string FileUrl(string fileName)
    {
        return new Uri(_platformUri, fileName).AbsoluteUri;
    }

    private static ReleaseManifest ParseManifest(byte[] manifestBytes)
    {
        try
        {
            return JsonSerializer.Deserialize<ReleaseManifest>(DecodeUtf8(manifestBytes), JsonSerializerOptions.Web)
                   ?? throw new ReleaseVerificationException(ReleaseVerificationFailure.InvalidManifest,
                       "The release manifest is empty.");
        }
        catch (JsonException ex)
        {
            throw new ReleaseVerificationException(ReleaseVerificationFailure.InvalidManifest,
                "The release manifest is not valid JSON.", ex);
        }
    }

    // Hashes and signatures cover the raw bytes; decoding drops a UTF-8 BOM that JSON parsers reject.
    private static string DecodeUtf8(byte[] bytes)
    {
        var span = bytes.AsSpan();
        if (span.StartsWith(Encoding.UTF8.Preamble))
            span = span[Encoding.UTF8.Preamble.Length..];
        return Encoding.UTF8.GetString(span);
    }
}
