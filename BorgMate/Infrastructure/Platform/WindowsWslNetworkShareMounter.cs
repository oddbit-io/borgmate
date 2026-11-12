using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
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
///     Mounts SMB shares for WSL-hosted borg: establishes the Windows-side SMB session via
///     WNetAddConnection2, then binds the UNC path inside WSL via a root drvfs mount. The
///     resulting path is WSL-native and is passed to borg without further conversion.
/// </summary>
public class WindowsWslNetworkShareMounter(
    ILogger<WindowsWslNetworkShareMounter> logger, ICredentialStore? keychain, WslHelper wsl,
    INotificationService? notifier = null) : NetworkShareMounterBase(logger, keychain, notifier)
{
    private const int ResourceTypeDisk = 1;
    private const int ConnectTemporary = 4;

    private const string DrvfsMountScript =
        "mkdir -p -- \"$2\"; mountpoint -q -- \"$2\" || mount -t drvfs \"$1\" \"$2\"";

    private const string CifsMountScript =
        "mkdir -p -- \"$2\"; mountpoint -q -- \"$2\" || mount -t cifs \"$1\" \"$2\" -o \"$3\"";

    private const char CifsOptionSeparator = ',';

    // The mount runs as root; uid/gid hand the files to the WSL user who runs borg, and the modes
    // keep every other WSL user out. Server-side ACLs still apply on top.
    private const string CifsAccessOptions = "file_mode=0600,dir_mode=0700,iocharset=utf8";
    private const int MountPointHashLength = 8;

    private const int NoError = 0;
    private const int ErrorAccessDenied = 5;
    private const int ErrorBadNetPath = 53;
    private const int ErrorBadNetName = 67;
    private const int ErrorInvalidPassword = 86;
    private const int ErrorNoNetOrBadPath = 1203;
    private const int ErrorSessionCredentialConflict = 1219;
    private const int ErrorLogonFailure = 1326;
    private const int ErrorNetworkUnreachable = 1231;

    protected override async Task<string?> TryGetExistingMountPointAsync(
        SmbLocation location, CancellationToken ct)
    {
        if (!await wsl.IsAvailableAsync(ct))
            throw new NetworkShareMountException(Strings.Get("Error.WslNotAvailable"));

        var mountPoint = BuildWslMountPoint(location);
        var result = await ProcessRunner.RunAsync("wsl", BuildMountPointCheckArguments(mountPoint), ct);
        return result.ExitCode == 0 ? mountPoint : null;
    }

    public override async Task<StorageSpaceSnapshot> GetStorageSpaceAsync(
        SmbLocation location,
        CancellationToken ct = default)
    {
        var result = await ProcessRunner.RunAsync(
            "wsl",
            BuildStorageQueryArguments(GetMountedPath(location)),
            ct);

        if (result.WasCancelled)
            throw new OperationCanceledException(ct);
        if (!result.Success)
            throw new DiskSpaceQueryException(result.ErrorMessage, result.ExitCode);

        return DiskSpaceProbe.ParseDfOutput(result.StandardOutput)
               ?? throw new DiskSpaceQueryException("WSL df returned unrecognized output.");
    }

    internal static IReadOnlyList<string> BuildStorageQueryArguments(string mountedPath)
    {
        return ["--exec", "df", "-P", "-k", mountedPath];
    }

    internal static IReadOnlyList<string> BuildMountPointCheckArguments(string mountPoint)
    {
        return ["--exec", "mountpoint", "-q", mountPoint];
    }

    /// <summary>The mount source, mountpoint and options travel as positional parameters of a constant script.</summary>
    internal static IReadOnlyList<string> BuildDrvfsMountArguments(string unc, string mountPoint)
    {
        return ["-u", "root", "--exec", "bash", "-c", DrvfsMountScript, "bash", unc, mountPoint];
    }

    internal static IReadOnlyList<string> BuildCifsMountArguments(string source, string mountPoint, string options)
    {
        return ["-u", "root", "--exec", "bash", "-c", CifsMountScript, "bash", source, mountPoint, options];
    }

    /// <summary>
    ///     Builds the comma-separated mount.cifs option list. A comma in the username would add options
    ///     of the caller's choosing, so such a username is rejected.
    /// </summary>
    internal static OneOf<string, MountError> BuildCifsOptions(SmbLocation location, bool authenticated,
        WslUserIds owner)
    {
        var access = $"uid={owner.Uid},gid={owner.Gid},{CifsAccessOptions}";
        if (!authenticated)
            return $"guest,{access}";

        if (location.Username.Contains(CifsOptionSeparator))
            return new MountError.Failed(string.Format(
                Strings.Get("Error.SmbUsernameInvalid"), location.DisplayName));

        return $"username={location.Username},{access}";
    }

    protected override async Task<OneOf<MountedShare, MountError>> MountOnceAsync(
        SmbLocation location, string? password, CancellationToken ct)
    {
        // On WSL 2 the distribution is a VM with its own network stack: a Windows-side SMB
        // session does not help it, and drvfs network mounts go through 9P with unverified
        // fsync semantics. Mount with the in-kernel cifs client instead.
        if (await wsl.IsWsl2Async(ct))
            return await MountInsideWsl2Async(location, password, ct);

        var unc = $@"\\{location.Host}\{location.Share}";

        if (password is not null)
        {
            var sessionError = await EstablishWindowsSessionAsync(location, unc, password, ct);
            if (sessionError is not null)
                return sessionError;
        }

        return await MountInsideWslAsync(location, unc, ct);
    }

    /// <summary>
    ///     Establishes the Windows-side SMB session via WNetAddConnection2 so the drvfs mount
    ///     can reuse it. The password goes through a typed API parameter — never a shell command
    ///     line — and failures come back as typed Win32 codes instead of localized text.
    ///     Returns null on success, or the failure.
    /// </summary>
    private async Task<MountError?> EstablishWindowsSessionAsync(
        SmbLocation location, string unc, string password, CancellationToken ct)
    {
        // WNetAddConnection2 blocks until the session is established or fails.
        var code = await Task.Run(() =>
        {
            var resource = new NetResource
            {
                Type = ResourceTypeDisk,
                RemoteName = unc
            };
            return WNetAddConnection2W(ref resource, password, location.Username, ConnectTemporary);
        }, ct);

        Logger.LogDebug("WNetAddConnection2 for {Share}: code={Code}", location.DisplayName, code);
        return MapWNetError(code, location);
    }

    /// <summary>Maps a WNetAddConnection2 result code to a mount error; null means the session is usable.</summary>
    internal static MountError? MapWNetError(int code, SmbLocation location)
    {
        return code switch
        {
            NoError => null,
            // A session with different credentials already exists — the mount can reuse it.
            ErrorSessionCredentialConflict => null,
            ErrorAccessDenied or ErrorInvalidPassword or ErrorLogonFailure => new MountError.AuthRejected(),
            ErrorBadNetPath or ErrorBadNetName or ErrorNoNetOrBadPath or ErrorNetworkUnreachable =>
                new MountError.Failed(
                    string.Format(Strings.Get("Error.SmbShareUnreachable"), location.DisplayName)),
            _ => new MountError.Failed(string.Format(
                Strings.Get("Error.SmbMountFailed"), location.DisplayName, $"error {code}"))
        };
    }

    private async Task<OneOf<MountedShare, MountError>> MountInsideWslAsync(
        SmbLocation location, string unc, CancellationToken ct)
    {
        var mountPoint = BuildWslMountPoint(location);
        var result = await ProcessRunner.RunAsync("wsl", BuildDrvfsMountArguments(unc, mountPoint), ct);

        Logger.LogDebug("WSL drvfs mount for {Share}: exit={ExitCode}, stderr={Stderr}",
            location.DisplayName, result.ExitCode, result.StandardError.Trim());

        if (result.ExitCode == 0)
            return new MountedShare(mountPoint);

        return new MountError.Failed(string.Format(
            Strings.Get("Error.SmbWslMountFailed"), location.DisplayName,
            $"{result.StandardError}\n{result.StandardOutput}".Trim()));
    }

    /// <summary>
    ///     WSL 2 mount path: the in-kernel cifs client connects to the share directly from the
    ///     Linux VM. The password travels via the PASSWD environment variable — read natively by
    ///     mount.cifs and propagated through WSLENV — so it never appears on a command line.
    /// </summary>
    private async Task<OneOf<MountedShare, MountError>> MountInsideWsl2Async(
        SmbLocation location, string? password, CancellationToken ct)
    {
        var mountPoint = BuildWslMountPoint(location);
        var source = $"//{location.Host}/{location.Share}";
        if (await wsl.GetDefaultUserIdsAsync(ct) is not { } owner)
            return new MountError.Failed(string.Format(Strings.Get("Error.SmbWslMountFailed"),
                location.DisplayName, "could not read the uid and gid of the WSL user"));
        if (!BuildCifsOptions(location, password is not null, owner).TryPickT0(out var options, out var optionsError))
            return optionsError;

        var environment = password is null
            ? null
            : WslHelper.ForwardEnvironment(new Dictionary<string, string> { ["PASSWD"] = password });

        var result = await ProcessRunner.RunAsync(
            "wsl", BuildCifsMountArguments(source, mountPoint, options), ct, environment: environment);

        Logger.LogDebug("WSL cifs mount for {Share}: exit={ExitCode}, stderr={Stderr}",
            location.DisplayName, result.ExitCode, result.StandardError.Trim());

        if (result.ExitCode == 0)
            return new MountedShare(mountPoint);

        // mount.cifs reports rejected credentials as "mount error(13): Permission denied".
        if (result.StandardError.Contains("error(13)"))
            return new MountError.AuthRejected();

        return new MountError.Failed(string.Format(
            Strings.Get("Error.SmbWslMountFailed"), location.DisplayName,
            $"{result.StandardError}\n{result.StandardOutput}".Trim()));
    }

    /// <summary>
    ///     Stable WSL mountpoint per share and account. Sanitizing maps different names to one string
    ///     (<c>share 1</c>, <c>share_1</c>), so a hash of the exact names keeps them apart; the account
    ///     is part of it because an existing mount is reused with the credentials it was made with.
    /// </summary>
    internal static string BuildWslMountPoint(SmbLocation location)
    {
        var identity = $"{location.Host}/{location.Share}/{location.Username}".ToLowerInvariant();
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        return $"/mnt/borgmate-smb/{Sanitize(location.Host)}_{Sanitize(location.Share)}-{hash[..MountPointHashLength]}";
    }

    private static string Sanitize(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var c in value.ToLowerInvariant())
            builder.Append(char.IsAsciiLetterOrDigit(c) || c is '.' or '-' ? c : '_');
        return builder.ToString();
    }

    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    private static extern int WNetAddConnection2W(
        ref NetResource netResource, string password, string userName, int flags);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NetResource
    {
        public int Scope;
        public int Type;
        public int DisplayType;
        public int Usage;
        public string? LocalName;
        public string RemoteName;
        public string? Comment;
        public string? Provider;
    }
}
