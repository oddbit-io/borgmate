using System.Runtime.InteropServices;
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

        var mountPoint = BuildWslMountPoint(location.Host, location.Share);
        var result = await ProcessRunner.RunAsync("wsl", $"-- mountpoint -q \"{mountPoint}\"", null, null, ct);
        return result.ExitCode == 0 ? mountPoint : null;
    }

    protected override async Task<OneOf<MountedShare, MountError>> MountOnceAsync(
        SmbLocation location, string? password, CancellationToken ct)
    {
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
        var mountPoint = BuildWslMountPoint(location.Host, location.Share);
        var command = $"mkdir -p '{mountPoint}'; mountpoint -q '{mountPoint}' || mount -t drvfs '{unc}' '{mountPoint}'";
        var result = await ProcessRunner.RunAsync("wsl",
            $"-u root -- bash -c {WslHelper.ShellEscapePublic(command)}", null, null, ct);

        Logger.LogDebug("WSL drvfs mount for {Share}: exit={ExitCode}, stderr={Stderr}",
            location.DisplayName, result.ExitCode, result.StandardError.Trim());

        if (result.ExitCode == 0)
            return new MountedShare(mountPoint);

        return new MountError.Failed(string.Format(
            Strings.Get("Error.SmbWslMountFailed"), location.DisplayName,
            $"{result.StandardError}\n{result.StandardOutput}".Trim()));
    }

    /// <summary>Stable per-share WSL mountpoint derived from sanitized host and share names.</summary>
    internal static string BuildWslMountPoint(string host, string share)
    {
        return $"/mnt/borgmate-smb/{Sanitize(host)}_{Sanitize(share)}";
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
