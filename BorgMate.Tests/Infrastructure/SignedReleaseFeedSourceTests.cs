using System.Text;
using BorgMate.Infrastructure.Updates;
using Velopack;
using Velopack.Exceptions;
using Velopack.Locators;

namespace BorgMate.Tests.Infrastructure;

public class SignedReleaseFeedSourceTests : IDisposable
{
    private const string PlatformUrl = "https://updates.test/releases/win-x64/";
    private const string ManifestUrl = PlatformUrl + "release-manifest.json";
    private const string SignatureUrl = ManifestUrl + ".asc";
    private const string FeedUrl = PlatformUrl + "releases.win.json";
    private const string PackageUrl = PlatformUrl + "BorgMate-9.9.9-full.nupkg";

    private readonly FakeFileDownloader _downloader = new();
    private readonly string _packagesDir = Directory.CreateTempSubdirectory("borgmate-update-tests-").FullName;

    public SignedReleaseFeedSourceTests()
    {
        _downloader.Files[ManifestUrl] = UpdateFixtures.Read("manifest.json");
        _downloader.Files[SignatureUrl] = UpdateFixtures.Read("manifest.json.current.asc");
        _downloader.Files[FeedUrl] = UpdateFixtures.Read("feed.json");
        _downloader.Files[PackageUrl] = Encoding.ASCII.GetBytes("package");
    }

    public void Dispose()
    {
        Directory.Delete(_packagesDir, true);
    }

    [Fact]
    public async Task CheckForUpdates_TrustedSignatureAndNewerVersion_OffersUpdate()
    {
        var update = await CreateManager().CheckForUpdatesAsync();

        Assert.NotNull(update);
        Assert.Equal("9.9.9", update.TargetFullRelease.Version.ToFullString());
    }

    [Fact]
    public async Task CheckForUpdates_TamperedManifest_OffersNoUpdate()
    {
        var manifest = UpdateFixtures.Read("manifest.json");
        manifest[^2] ^= 1;
        _downloader.Files[ManifestUrl] = manifest;

        await AssertRejectedAsync(CreateManager(), ReleaseVerificationFailure.InvalidSignature);
    }

    [Fact]
    public async Task CheckForUpdates_ForeignSignature_OffersNoUpdate()
    {
        _downloader.Files[SignatureUrl] = UpdateFixtures.Read("manifest.json.foreign.asc");

        await AssertRejectedAsync(CreateManager(), ReleaseVerificationFailure.UntrustedSignature);
    }

    [Fact]
    public async Task CheckForUpdates_MissingSignature_OffersNoUpdate()
    {
        _downloader.Files.Remove(SignatureUrl);

        await AssertRejectedAsync(CreateManager(), ReleaseVerificationFailure.SignatureMissing);
    }

    [Fact]
    public async Task CheckForUpdates_FeedChangedAfterSigning_OffersNoUpdate()
    {
        _downloader.Files[FeedUrl] = Encoding.UTF8.GetBytes("""{"assets":[]}""");

        await AssertRejectedAsync(CreateManager(), ReleaseVerificationFailure.FeedHashMismatch);
    }

    [Fact]
    public async Task CheckForUpdates_ManifestOfAnotherPlatform_OffersNoUpdate()
    {
        await AssertRejectedAsync(CreateManager(platform: "win-arm64"), ReleaseVerificationFailure.ManifestMismatch);
    }

    [Fact]
    public async Task CheckForUpdates_SignatureBySubkeyMissingFromEmbeddedKey_OffersNoUpdate()
    {
        _downloader.Files[SignatureUrl] = UpdateFixtures.Read("manifest.json.next.asc");

        await AssertRejectedAsync(CreateManager(), ReleaseVerificationFailure.UntrustedSignature);
    }

    [Theory]
    [InlineData("release-keys-current.asc")]
    [InlineData("release-keys-next.asc")]
    public async Task CheckForUpdates_KeyRotationSignedByBothKeys_OffersUpdateToEitherClient(string embeddedKeys)
    {
        _downloader.Files[SignatureUrl] = UpdateFixtures.Read("manifest.json.both.asc");

        var update = await CreateManager(embeddedKeys: embeddedKeys).CheckForUpdatesAsync();

        Assert.NotNull(update);
        Assert.Equal("9.9.9", update.TargetFullRelease.Version.ToFullString());
    }

    [Fact]
    public async Task DownloadUpdates_PackageMatchingSignedFeed_StoresPackage()
    {
        var manager = CreateManager();
        var update = await manager.CheckForUpdatesAsync();
        Assert.NotNull(update);

        await manager.DownloadUpdatesAsync(update);

        Assert.Equal("package", await File.ReadAllTextAsync(Path.Combine(_packagesDir, "BorgMate-9.9.9-full.nupkg")));
    }

    [Fact]
    public async Task DownloadUpdates_PackageNotMatchingSignedFeed_FailsChecksum()
    {
        var manager = CreateManager();
        var update = await manager.CheckForUpdatesAsync();
        Assert.NotNull(update);
        _downloader.Files[PackageUrl] = Encoding.ASCII.GetBytes("pockage");

        await Assert.ThrowsAsync<ChecksumFailedException>(() => manager.DownloadUpdatesAsync(update));
    }

    [Fact]
    public async Task DownloadUpdates_CachedPackageNotMatchingSignedFeed_FailsChecksum()
    {
        var manager = CreateManager();
        var update = await manager.CheckForUpdatesAsync();
        Assert.NotNull(update);
        await File.WriteAllTextAsync(Path.Combine(_packagesDir, update.TargetFullRelease.FileName), "pockage");

        await Assert.ThrowsAsync<ChecksumFailedException>(() => manager.DownloadUpdatesAsync(update));
    }

    [Fact]
    public async Task VerifyRelease_PackageChangedAfterDownload_FailsChecksum()
    {
        var manager = CreateManager();
        var update = await manager.CheckForUpdatesAsync();
        Assert.NotNull(update);
        await manager.DownloadUpdatesAsync(update);
        await File.WriteAllTextAsync(Path.Combine(_packagesDir, update.TargetFullRelease.FileName), "pockage");

        await Assert.ThrowsAsync<ChecksumFailedException>(() => manager.VerifyReleaseAsync(update.TargetFullRelease));
    }

    [Fact]
    public async Task DownloadUpdates_ReleaseWithoutSha256_IsRejected()
    {
        var manager = CreateManager();
        var update = await manager.CheckForUpdatesAsync();
        Assert.NotNull(update);
        update.TargetFullRelease.SHA256 = "";

        await Assert.ThrowsAsync<ChecksumFailedException>(() => manager.DownloadUpdatesAsync(update));
        Assert.False(File.Exists(Path.Combine(_packagesDir, update.TargetFullRelease.FileName)));
    }

    [Fact]
    public void CreateForReleases_LoadsEmbeddedReleaseKey()
    {
        ReleaseSignatureVerifier.CreateForReleases();
    }

    private VerifiedUpdateManager CreateManager(string platform = "win-x64", string embeddedKeys = "release-keys-current.asc")
    {
        using var keys = new MemoryStream(UpdateFixtures.Read(embeddedKeys));
        var verifier = new ReleaseSignatureVerifier(keys);
        var source = new SignedReleaseFeedSource(new Uri(PlatformUrl), platform, _downloader, verifier);
        return new VerifiedUpdateManager(source, new TestVelopackLocator("BorgMate", "1.0.0", _packagesDir, null),
            explicitChannel: "win");
    }

    private static async Task AssertRejectedAsync(UpdateManager manager, ReleaseVerificationFailure failure)
    {
        var ex = await Assert.ThrowsAsync<ReleaseVerificationException>(manager.CheckForUpdatesAsync);
        Assert.Equal(failure, ex.Failure);
    }
}
