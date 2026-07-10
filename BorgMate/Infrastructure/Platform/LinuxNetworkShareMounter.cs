using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using BorgMate.Domain.Repositories;
using BorgMate.Infrastructure.Notifications;
using BorgMate.Persistence.Keychain;
using Microsoft.Extensions.Logging;
using OneOf;

namespace BorgMate.Infrastructure.Platform;

/// <summary>
///     Mounts SMB shares through the native GIO API — the same gvfs daemons `gio mount`
///     uses, still a user-level mount without root — but failures come back as typed
///     GError codes instead of localized CLI text. The mountpoint lives under
///     GLib's user runtime directory and is resolved by directory-name convention.
/// </summary>
public class LinuxNetworkShareMounter(
    ILogger<LinuxNetworkShareMounter> logger, ICredentialStore? keychain,
    INotificationService? notifier = null) : NetworkShareMounterBase(logger, keychain, notifier)
{
    protected override Task<string?> TryGetExistingMountPointAsync(SmbLocation location, CancellationToken ct)
    {
        return Task.FromResult(TryFindGvfsMountPath(location));
    }

    protected override Task<OneOf<MountedShare, MountError>> MountOnceAsync(
        SmbLocation location, string? password, CancellationToken ct)
    {
        // The GIO call drives its own GMainLoop until the async mount completes.
        return Task.Run(() => MountViaGio(location, password), ct);
    }

    private OneOf<MountedShare, MountError> MountViaGio(SmbLocation location, string? password)
    {
        try
        {
            return MountEnclosingVolume(location, password);
        }
        catch (DllNotFoundException)
        {
            return new MountError.Failed(Strings.Get("Error.SmbGvfsMissing"));
        }
        catch (EntryPointNotFoundException)
        {
            return new MountError.Failed(Strings.Get("Error.SmbGvfsMissing"));
        }
    }

    private OneOf<MountedShare, MountError> MountEnclosingVolume(SmbLocation location, string? password)
    {
        var context = GioNative.g_main_context_new();
        GioNative.g_main_context_push_thread_default(context);
        var loop = GioNative.g_main_loop_new(context, false);
        var file = GioNative.g_file_new_for_uri(
            $"smb://{location.Host}/{Uri.EscapeDataString(location.Share)}");
        var operation = GioNative.g_mount_operation_new();
        var errorPtr = IntPtr.Zero;
        var passwordAttempts = 0;

        // gvfs requests credentials via the ask-password signal; answer once with the
        // configured values and abort on a second request — it means they were rejected.
        GioNative.AskPasswordHandler askPassword = (op, _, _, _, _, _) =>
        {
            if (++passwordAttempts > 1)
            {
                GioNative.g_mount_operation_reply(op, GioNative.MountOperationAborted);
                return;
            }

            if (password is null)
            {
                GioNative.g_mount_operation_set_anonymous(op, true);
            }
            else
            {
                GioNative.g_mount_operation_set_username(op, location.Username);
                GioNative.g_mount_operation_set_password(op, password);
            }

            GioNative.g_mount_operation_reply(op, GioNative.MountOperationHandled);
        };
        GioNative.AsyncReadyCallback onReady = (source, result, _) =>
        {
            GioNative.g_file_mount_enclosing_volume_finish(source, result, out errorPtr);
            GioNative.g_main_loop_quit(loop);
        };

        try
        {
            GioNative.g_signal_connect_data(operation, "ask-password",
                Marshal.GetFunctionPointerForDelegate(askPassword), IntPtr.Zero, IntPtr.Zero, 0);
            GioNative.g_file_mount_enclosing_volume(file, 0, operation, IntPtr.Zero,
                Marshal.GetFunctionPointerForDelegate(onReady), IntPtr.Zero);
            GioNative.g_main_loop_run(loop);
            GC.KeepAlive(askPassword);
            GC.KeepAlive(onReady);

            return ClassifyMountResult(location, errorPtr, passwordAttempts > 1);
        }
        finally
        {
            GioNative.g_object_unref(operation);
            GioNative.g_object_unref(file);
            GioNative.g_main_loop_unref(loop);
            GioNative.g_main_context_pop_thread_default(context);
            GioNative.g_main_context_unref(context);
        }
    }

    private OneOf<MountedShare, MountError> ClassifyMountResult(
        SmbLocation location, IntPtr errorPtr, bool credentialsRejected)
    {
        if (errorPtr == IntPtr.Zero)
            return ResolveMountPoint(location, string.Empty);

        var error = Marshal.PtrToStructure<GioNative.GError>(errorPtr);
        var message = Marshal.PtrToStringUTF8(error.Message) ?? string.Empty;
        var isIoError = error.Domain == GioNative.g_io_error_quark();
        GioNative.g_error_free(errorPtr);

        Logger.LogDebug("GIO mount for {Share}: domain={Domain}, code={Code}, message={Message}",
            location.DisplayName, error.Domain, error.Code, message);

        if (isIoError && error.Code == GioNative.IoErrorAlreadyMounted)
            return ResolveMountPoint(location, message);

        if (credentialsRejected)
            return new MountError.AuthRejected();

        return ClassifyGioError(isIoError, error.Code, message, location);
    }

    internal static MountError ClassifyGioError(bool isIoError, int code, string message, SmbLocation location)
    {
        if (!isIoError)
            return new MountError.Failed(string.Format(
                Strings.Get("Error.SmbMountFailed"), location.DisplayName, message));

        return code switch
        {
            GioNative.IoErrorPermissionDenied or GioNative.IoErrorFailedHandled =>
                new MountError.AuthRejected(),
            GioNative.IoErrorNotSupported or GioNative.IoErrorDbusError =>
                new MountError.Failed(Strings.Get("Error.SmbGvfsMissing")),
            GioNative.IoErrorTimedOut or GioNative.IoErrorHostNotFound or GioNative.IoErrorHostUnreachable
                or GioNative.IoErrorNetworkUnreachable or GioNative.IoErrorConnectionRefused =>
                new MountError.Failed(
                    string.Format(Strings.Get("Error.SmbShareUnreachable"), location.DisplayName)),
            _ => new MountError.Failed(string.Format(
                Strings.Get("Error.SmbMountFailed"), location.DisplayName, message))
        };
    }

    private static OneOf<MountedShare, MountError> ResolveMountPoint(SmbLocation location, string detail)
    {
        var mountPoint = TryFindGvfsMountPath(location);
        if (mountPoint is not null)
            return new MountedShare(mountPoint);

        return new MountError.Failed(string.Format(
            Strings.Get("Error.SmbMountFailed"), location.DisplayName, detail));
    }

    private static string? TryFindGvfsMountPath(SmbLocation location)
    {
        var runtimeDirectory = GetUserRuntimeDirectory();
        return runtimeDirectory is null ? null : TryFindGvfsMountPath(location, runtimeDirectory);
    }

    private static string? GetUserRuntimeDirectory()
    {
        return Marshal.PtrToStringUTF8(GioNative.g_get_user_runtime_dir());
    }

    internal static string? TryFindGvfsMountPath(SmbLocation location, string runtimeDirectory)
    {
        var expected = BuildGvfsMountPath(runtimeDirectory, location.Host, location.Share);
        if (Directory.Exists(expected))
            return expected;

        // The gvfs entry can carry extra pairs (e.g. ",user=") — match by parsed key-value pairs.
        var gvfsRoot = Path.Combine(runtimeDirectory, "gvfs");
        if (!Directory.Exists(gvfsRoot))
            return null;

        foreach (var dir in Directory.EnumerateDirectories(gvfsRoot))
            if (GvfsEntryMatches(Path.GetFileName(dir), location.Host, location.Share))
                return dir;

        return null;
    }

    internal static string BuildGvfsMountPath(string runtimeDirectory, string host, string share)
    {
        return Path.Combine(runtimeDirectory, "gvfs",
            $"smb-share:server={host.ToLowerInvariant()},share={share.ToLowerInvariant()}");
    }

    internal static bool GvfsEntryMatches(string entryName, string host, string share)
    {
        const string prefix = "smb-share:";
        if (!entryName.StartsWith(prefix, StringComparison.Ordinal))
            return false;

        var serverMatches = false;
        var shareMatches = false;
        foreach (var pair in entryName[prefix.Length..].Split(','))
        {
            var separatorIdx = pair.IndexOf('=');
            if (separatorIdx <= 0)
                continue;

            var key = pair[..separatorIdx];
            var value = Uri.UnescapeDataString(pair[(separatorIdx + 1)..]);
            if (key == "server")
                serverMatches = string.Equals(value, host, StringComparison.OrdinalIgnoreCase);
            else if (key == "share")
                shareMatches = string.Equals(value, share, StringComparison.OrdinalIgnoreCase);
        }

        return serverMatches && shareMatches;
    }
}
