using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using BorgMate.Domain.Repositories;
using BorgMate.Infrastructure.Borg;
using BorgMate.Infrastructure.Notifications;
using BorgMate.Persistence.Keychain;
using Microsoft.Extensions.Logging;
using OneOf;

namespace BorgMate.Infrastructure.Platform;

/// <summary>
///     Mounts SMB shares via the NetFS framework. NetFSMountURLSync handles /Volumes naming
///     collisions and returns the actual mountpoint; the NoUI option keeps system dialogs
///     suppressed because BorgMate owns the credential prompt.
/// </summary>
public class MacOsNetworkShareMounter(
    ILogger<MacOsNetworkShareMounter> logger, ICredentialStore? keychain,
    INotificationService? notifier = null) : NetworkShareMounterBase(logger, keychain, notifier)
{
    private const string NetFsLib = "/System/Library/Frameworks/NetFS.framework/NetFS";

    private const int Eexist = 17;
    private const int MaxMountPointBytes = 1024;

    // NetFS option keys and values as defined in NetFS.h.
    private const string UiOptionKey = "UIOption";
    private const string UiOptionNoUi = "NoUI";
    private const string UseGuestKey = "Guest";
    private const string SoftMountKey = "SoftMount";

    private static readonly int[] AuthErrorCodes = [1, 13, 80]; // EPERM, EACCES, EAUTH
    private static readonly int[] UnreachableErrorCodes =
        [2, 19, 50, 51, 54, 60, 61, 64, 65]; // ENOENT, ENODEV, ENETDOWN, ENETUNREACH, ECONNRESET, ETIMEDOUT, ECONNREFUSED, EHOSTDOWN, EHOSTUNREACH

    protected override async Task<string?> TryGetExistingMountPointAsync(
        SmbLocation location, CancellationToken ct)
    {
        var result = await ProcessRunner.RunAsync("mount", [], ct);
        return result.ExitCode == 0
            ? FindSmbMountPoint(result.StandardOutput, location.Host, location.Share)
            : null;
    }

    /// <summary>
    ///     Finds the mountpoint of an already-mounted share in `mount` output lines like
    ///     "//user@host/share on /Volumes/share (smbfs, ...)". Bonjour hosts appear with
    ///     a "._smb._tcp.local" suffix that is stripped before comparison.
    /// </summary>
    internal static string? FindSmbMountPoint(string mountOutput, string host, string share)
    {
        foreach (var line in mountOutput.Split('\n'))
        {
            if (!line.StartsWith("//", StringComparison.Ordinal))
                continue;

            var optionsIdx = line.LastIndexOf(" (", StringComparison.Ordinal);
            if (optionsIdx < 0 || !line[optionsIdx..].Contains("smbfs", StringComparison.Ordinal))
                continue;

            var head = line[..optionsIdx];
            var onIdx = head.IndexOf(" on ", StringComparison.Ordinal);
            if (onIdx < 0)
                continue;

            var source = head[2..onIdx];
            var mountPoint = head[(onIdx + 4)..];

            var atIdx = source.IndexOf('@');
            var hostAndShare = atIdx >= 0 ? source[(atIdx + 1)..] : source;
            var slashIdx = hostAndShare.IndexOf('/');
            if (slashIdx <= 0)
                continue;

            var mountedHost = hostAndShare[..slashIdx];
            const string bonjourSuffix = "._smb._tcp.local";
            if (mountedHost.EndsWith(bonjourSuffix, StringComparison.OrdinalIgnoreCase))
                mountedHost = mountedHost[..^bonjourSuffix.Length];

            var mountedShare = Uri.UnescapeDataString(hostAndShare[(slashIdx + 1)..]);

            if (string.Equals(mountedHost, host, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(mountedShare, share, StringComparison.OrdinalIgnoreCase))
                return mountPoint;
        }

        return null;
    }

    protected override Task<OneOf<MountedShare, MountError>> MountOnceAsync(
        SmbLocation location, string? password, CancellationToken ct)
    {
        // NetFSMountURLSync blocks until the mount completes or times out.
        return Task.Run(() => MountViaNetFs(location, password), ct);
    }

    private OneOf<MountedShare, MountError> MountViaNetFs(SmbLocation location, string? password)
    {
        var url = $"smb://{location.Host}/{Uri.EscapeDataString(location.Share)}";
        using var cf = new CoreFoundationScope();

        var cfUrl = cf.Own(CoreFoundationNative.CFURLCreateWithString(IntPtr.Zero,
            cf.Own(CoreFoundationNative.CreateString(url)), IntPtr.Zero));
        if (cfUrl == IntPtr.Zero)
            return new MountError.Failed(
                string.Format(Strings.Get("Error.SmbMountFailed"), location.DisplayName, url));

        var cfUser = location.Username.Length > 0
            ? cf.Own(CoreFoundationNative.CreateString(location.Username))
            : IntPtr.Zero;
        var cfPassword = password is not null
            ? cf.Own(CoreFoundationNative.CreateString(password))
            : IntPtr.Zero;

        var openOptions = cf.Own(CoreFoundationNative.CreateMutableDictionary());
        CoreFoundationNative.CFDictionarySetValue(openOptions,
            cf.Own(CoreFoundationNative.CreateString(UiOptionKey)),
            cf.Own(CoreFoundationNative.CreateString(UiOptionNoUi)));
        if (location.Username.Length == 0)
            CoreFoundationNative.CFDictionarySetValue(openOptions,
                cf.Own(CoreFoundationNative.CreateString(UseGuestKey)), CoreFoundationNative.BooleanTrue);

        var mountOptions = cf.Own(CoreFoundationNative.CreateMutableDictionary());
        CoreFoundationNative.CFDictionarySetValue(mountOptions,
            cf.Own(CoreFoundationNative.CreateString(SoftMountKey)), CoreFoundationNative.BooleanTrue);

        var rc = NetFSMountURLSync(cfUrl, IntPtr.Zero, cfUser, cfPassword,
            openOptions, mountOptions, out var mounts);
        cf.Own(mounts);
        Logger.LogDebug("NetFSMountURLSync for {Share}: rc={Rc}", location.DisplayName, rc);

        // EEXIST still reports the existing mountpoint in the out array.
        if (rc is 0 or Eexist && ReadFirstMountPoint(mounts) is { } mountPoint)
            return new MountedShare(mountPoint);

        if (Array.IndexOf(AuthErrorCodes, rc) >= 0)
            return new MountError.AuthRejected();

        if (Array.IndexOf(UnreachableErrorCodes, rc) >= 0)
            return new MountError.Failed(
                string.Format(Strings.Get("Error.SmbShareUnreachable"), location.DisplayName));

        return new MountError.Failed(
            string.Format(Strings.Get("Error.SmbMountFailed"), location.DisplayName, $"error {rc}"));
    }

    private static string? ReadFirstMountPoint(IntPtr mounts)
    {
        if (mounts == IntPtr.Zero || CoreFoundationNative.CFArrayGetCount(mounts) == 0)
            return null;

        var cfPath = CoreFoundationNative.CFArrayGetValueAtIndex(mounts, 0);
        return cfPath == IntPtr.Zero ? null : CoreFoundationNative.GetString(cfPath, MaxMountPointBytes);
    }

    [DllImport(NetFsLib)]
    private static extern int NetFSMountURLSync(IntPtr url, IntPtr mountPath, IntPtr user, IntPtr password,
        IntPtr openOptions, IntPtr mountOptions, out IntPtr mountPoints);
}
