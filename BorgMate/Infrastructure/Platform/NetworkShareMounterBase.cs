using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using BorgMate.Domain.Repositories;
using BorgMate.Infrastructure.Notifications;
using BorgMate.Persistence.Keychain;
using BorgMate.Presentation.Dialogs;
using BorgMate.Runtime.Orchestration;
using BorgMate.Runtime.Security;
using Microsoft.Extensions.Logging;
using OneOf;

namespace BorgMate.Infrastructure.Platform;

/// <summary>
///     Shared SMB mount workflow: reuse an existing mount, otherwise mount with a password
///     from the keychain or a UI prompt, retrying on rejected credentials. Platform
///     subclasses implement the actual mount and existing-mount detection.
/// </summary>
public abstract class NetworkShareMounterBase(
    ILogger logger, ICredentialStore? keychain,
    INotificationService? notifier = null) : INetworkShareMounter
{
    private const int MaxPasswordAttempts = 3;

    // global mount lock - mounts are rare; per-share locks if it ever contends.
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ConcurrentDictionary<string, string> _mountPoints = new(StringComparer.Ordinal);

    protected ILogger Logger => logger;

    public async Task<string> EnsureMountedAsync(SmbLocation location, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_mountPoints.TryGetValue(location.KeychainKey, out var cached) && Directory.Exists(cached))
                return cached;

            var existing = await TryGetExistingMountPointAsync(location, ct);
            if (existing is not null)
            {
                logger.LogDebug("SMB share {Share} already mounted at {MountPoint}", location.DisplayName, existing);
                _mountPoints[location.KeychainKey] = existing;
                return existing;
            }

            var mountPoint = await MountWithCredentialRetriesAsync(location, ct);
            logger.LogInformation("Mounted SMB share {Share} at {MountPoint}", location.DisplayName, mountPoint);
            _mountPoints[location.KeychainKey] = mountPoint;
            return mountPoint;
        }
        finally
        {
            _gate.Release();
        }
    }

    public string GetMountedPath(SmbLocation location)
    {
        if (!_mountPoints.TryGetValue(location.KeychainKey, out var mountPoint))
            throw new InvalidOperationException(
                $"SMB share {location.DisplayName} has not been mounted in this session.");

        // Mountpoints are unix-style on every platform (WSL paths on Windows), so join with '/'.
        return location.PathInShare.Length == 0
            ? mountPoint
            : $"{mountPoint.TrimEnd('/')}/{location.PathInShare}";
    }

    public virtual Task<StorageSpaceSnapshot> GetStorageSpaceAsync(
        SmbLocation location,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(DiskSpaceProbe.GetVolumeSpace(GetMountedPath(location)));
    }

    /// <summary>Returns the mountpoint when the share is already mounted, otherwise null.</summary>
    protected abstract Task<string?> TryGetExistingMountPointAsync(SmbLocation location, CancellationToken ct);

    /// <summary>Performs one mount attempt. A null password means guest/anonymous access.</summary>
    protected abstract Task<OneOf<MountedShare, MountError>> MountOnceAsync(
        SmbLocation location, string? password, CancellationToken ct);

    private async Task<string> MountWithCredentialRetriesAsync(SmbLocation location, CancellationToken ct)
    {
        if (location.Username.Length == 0)
        {
            var guest = await MountOnceAsync(location, null, ct);
            if (guest.TryPickT0(out var guestMounted, out var guestError))
                return guestMounted.MountPoint;

            throw ToMountException(guestError, location);
        }

        for (var attempt = 0; attempt < MaxPasswordAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            var password = await GetOrPromptPasswordAsync(location, attempt > 0);
            if (password is null)
                throw new OperationCanceledException();

            var result = await MountOnceAsync(location, password, ct);
            if (result.TryPickT0(out var mounted, out var error))
                return mounted.MountPoint;

            if (error is not MountError.AuthRejected)
                throw ToMountException(error, location);

            logger.LogWarning("SMB credentials rejected for {Share}", location.DisplayName);
            if (keychain is not null)
                await keychain.DeletePassphraseAsync(location.KeychainKey);
        }

        throw new NetworkShareMountException(
            string.Format(Strings.Get("Error.SmbCredentialsRejected"), location.DisplayName));
    }

    private static NetworkShareMountException ToMountException(MountError error, SmbLocation location)
    {
        return new NetworkShareMountException(error switch
        {
            MountError.Failed failed => failed.Message,
            _ => string.Format(Strings.Get("Error.SmbCredentialsRejected"), location.DisplayName)
        });
    }

    private async Task<string?> GetOrPromptPasswordAsync(SmbLocation location, bool previousRejected)
    {
        if (!previousRejected && keychain is not null)
        {
            var stored = await keychain.GetPassphraseAsync(location.KeychainKey);
            if (!string.IsNullOrWhiteSpace(stored))
                return stored;
        }

        // Scheduled runs have nobody at the screen — fail into the journal instead of
        // blocking on the dialog. Covers the rejected-keychain-password retry too.
        if (!OperationInteractivity.IsInteractive)
            throw new CredentialsRequiredException(
                string.Format(Strings.Get("Error.SmbPasswordRequired.Unattended"), location.DisplayName));

        var message = previousRejected
            ? Strings.Get("WrongSmbPassword")
            : string.Format(Strings.Get("EnterSmbPassword"), location.DisplayName);

        // Dialog must be created and shown on the UI thread
        var (result, saveToKeychain) = await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var window = DialogHelper.GetMainWindow();
            if (window is null) return ((string?)null, false);

            return await DialogHelper.ShowPasswordDialogAsync(window,
                Strings.Get("SmbPasswordRequired"),
                message,
                Strings.Get("WatermarkSmbPassword"));
        });

        if (string.IsNullOrWhiteSpace(result))
            return null;

        if (saveToKeychain && keychain is not null)
            await keychain.TrySetPassphraseAsync(notifier, location.KeychainKey, result);

        return result;
    }
}
