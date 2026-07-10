using System;
using System.Collections.Generic;
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
    private const string CoreFoundationLib =
        "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    private const uint KCfStringEncodingUtf8 = 0x08000100;
    private const int Eexist = 17;

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
        var result = await ProcessRunner.RunAsync("mount", "", null, null, ct);
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
        var created = new List<IntPtr>();
        var mounts = IntPtr.Zero;
        try
        {
            var cfUrl = CFURLCreateWithString(IntPtr.Zero, Retain(created, CreateCfString(url)), IntPtr.Zero);
            if (cfUrl == IntPtr.Zero)
                return new MountError.Failed(
                    string.Format(Strings.Get("Error.SmbMountFailed"), location.DisplayName, url));
            created.Add(cfUrl);

            var cfUser = location.Username.Length > 0
                ? Retain(created, CreateCfString(location.Username))
                : IntPtr.Zero;
            var cfPassword = password is not null
                ? Retain(created, CreateCfString(password))
                : IntPtr.Zero;

            var openOptions = Retain(created, CreateCfDictionary());
            CFDictionarySetValue(openOptions,
                Retain(created, CreateCfString(UiOptionKey)),
                Retain(created, CreateCfString(UiOptionNoUi)));
            if (location.Username.Length == 0)
                CFDictionarySetValue(openOptions, Retain(created, CreateCfString(UseGuestKey)), KCfBooleanTrue);

            var mountOptions = Retain(created, CreateCfDictionary());
            CFDictionarySetValue(mountOptions, Retain(created, CreateCfString(SoftMountKey)), KCfBooleanTrue);

            var rc = NetFSMountURLSync(cfUrl, IntPtr.Zero, cfUser, cfPassword,
                openOptions, mountOptions, out mounts);
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
        finally
        {
            if (mounts != IntPtr.Zero)
                CFRelease(mounts);
            for (var i = created.Count - 1; i >= 0; i--)
                CFRelease(created[i]);
        }
    }

    private static IntPtr Retain(List<IntPtr> created, IntPtr value)
    {
        created.Add(value);
        return value;
    }

    private static string? ReadFirstMountPoint(IntPtr mounts)
    {
        if (mounts == IntPtr.Zero || CFArrayGetCount(mounts) == 0)
            return null;

        var cfPath = CFArrayGetValueAtIndex(mounts, 0);
        if (cfPath == IntPtr.Zero)
            return null;

        var buffer = new byte[1024];
        if (!CFStringGetCString(cfPath, buffer, buffer.Length, KCfStringEncodingUtf8))
            return null;

        var length = Array.IndexOf(buffer, (byte)0);
        return System.Text.Encoding.UTF8.GetString(buffer, 0, length < 0 ? buffer.Length : length);
    }

    private static IntPtr CreateCfString(string value)
    {
        return CFStringCreateWithCString(IntPtr.Zero, value, KCfStringEncodingUtf8);
    }

    private static IntPtr CreateCfDictionary()
    {
        var cf = NativeLibrary.Load(CoreFoundationLib);
        return CFDictionaryCreateMutable(IntPtr.Zero, 0,
            NativeLibrary.GetExport(cf, "kCFTypeDictionaryKeyCallBacks"),
            NativeLibrary.GetExport(cf, "kCFTypeDictionaryValueCallBacks"));
    }

    private static IntPtr KCfBooleanTrue =>
        Marshal.ReadIntPtr(NativeLibrary.GetExport(NativeLibrary.Load(CoreFoundationLib), "kCFBooleanTrue"));

    [DllImport(NetFsLib)]
    private static extern int NetFSMountURLSync(IntPtr url, IntPtr mountPath, IntPtr user, IntPtr password,
        IntPtr openOptions, IntPtr mountOptions, out IntPtr mountPoints);

    [DllImport(CoreFoundationLib)]
    private static extern IntPtr CFStringCreateWithCString(IntPtr allocator,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string value, uint encoding);

    [DllImport(CoreFoundationLib)]
    private static extern IntPtr CFURLCreateWithString(IntPtr allocator, IntPtr urlString, IntPtr baseUrl);

    [DllImport(CoreFoundationLib)]
    private static extern IntPtr CFDictionaryCreateMutable(IntPtr allocator, nint capacity,
        IntPtr keyCallbacks, IntPtr valueCallbacks);

    [DllImport(CoreFoundationLib)]
    private static extern void CFDictionarySetValue(IntPtr dictionary, IntPtr key, IntPtr value);

    [DllImport(CoreFoundationLib)]
    private static extern nint CFArrayGetCount(IntPtr array);

    [DllImport(CoreFoundationLib)]
    private static extern IntPtr CFArrayGetValueAtIndex(IntPtr array, nint index);

    [DllImport(CoreFoundationLib)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool CFStringGetCString(IntPtr value, byte[] buffer, nint bufferSize, uint encoding);

    [DllImport(CoreFoundationLib)]
    private static extern void CFRelease(IntPtr value);
}
