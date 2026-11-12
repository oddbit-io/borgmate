using System;
using System.Threading;
using System.Threading.Tasks;
using Velopack;
using Velopack.Exceptions;
using Velopack.Locators;
using Velopack.Sources;

namespace BorgMate.Infrastructure.Updates;

/// <summary>
///     Installs only full packages that match the signed feed, never a lower version, and checks
///     cached packages again before use.
/// </summary>
public class VerifiedUpdateManager : UpdateManager
{
    private const int DisableDeltas = -1;

    public VerifiedUpdateManager(IUpdateSource source, IVelopackLocator? locator = null, string? explicitChannel = null)
        : base(source, new UpdateOptions
        {
            ExplicitChannel = explicitChannel,
            AllowVersionDowngrade = false,
            MaximumDeltasBeforeFallback = DisableDeltas
        }, locator)
    {
    }

    public override async Task DownloadUpdatesAsync(UpdateInfo updates, Action<int>? progress = null,
        CancellationToken cancelToken = default)
    {
        RequireSha256(updates.TargetFullRelease);
        await base.DownloadUpdatesAsync(updates, progress, cancelToken).ConfigureAwait(false);
        await VerifyReleaseAsync(updates.TargetFullRelease).ConfigureAwait(false);
    }

    /// <summary>Revalidates the package immediately before handing it to the installer.</summary>
    public Task VerifyReleaseAsync(VelopackAsset release)
    {
        RequireSha256(release);
        return VerifyPackageChecksumAsync(release);
    }

    private void RequireSha256(VelopackAsset release)
    {
        if (string.IsNullOrWhiteSpace(release.SHA256))
            throw new ChecksumFailedException(release.FileName,
                "The signed release feed must contain a SHA-256 package hash.");
    }
}
