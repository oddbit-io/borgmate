using System;
using System.Threading;
using System.Threading.Tasks;
using Velopack;
using Velopack.Exceptions;
using Velopack.Locators;
using Velopack.Sources;

namespace BorgMate.Infrastructure.Updates;

/// <summary>Checks the final package against the signed feed, including cached and reconstructed packages.</summary>
public class VerifiedUpdateManager : UpdateManager
{
    public VerifiedUpdateManager(IUpdateSource source, UpdateOptions? options = null, IVelopackLocator? locator = null)
        : base(source, options, locator)
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

    protected override async Task DownloadAndApplyDeltaUpdates(UpdateInfo updates, string targetFile,
        Action<int> progress, CancellationToken cancelToken)
    {
        await base.DownloadAndApplyDeltaUpdates(updates, targetFile, progress, cancelToken).ConfigureAwait(false);
        // Validate reconstruction before Velopack extracts and replaces the updater from the package.
        await VerifyPackageChecksumAsync(updates.TargetFullRelease, targetFile).ConfigureAwait(false);
    }

    private void RequireSha256(VelopackAsset release)
    {
        if (string.IsNullOrWhiteSpace(release.SHA256))
            throw new ChecksumFailedException(release.FileName,
                "The signed release feed must contain a SHA-256 package hash.");
    }
}
