using BorgMate.Domain.Repositories;
using BorgMate.Infrastructure.Platform;
using BorgMate.Localization;

namespace BorgMate.Tests.Infrastructure;

public class NetworkShareMounterTests
{
    private static readonly SmbLocation Location = new() { Host = "nas", Share = "backups" };

    // --- Windows: WNetAddConnection2 result mapping ---

    [Theory]
    [InlineData(0)] // NO_ERROR
    [InlineData(1219)] // ERROR_SESSION_CREDENTIAL_CONFLICT: an existing session is reused
    public void MapWNetError_UsableSession_ReturnsNull(int code)
    {
        Assert.Null(WindowsWslNetworkShareMounter.MapWNetError(code, Location));
    }

    [Theory]
    [InlineData(5)] // ERROR_ACCESS_DENIED
    [InlineData(86)] // ERROR_INVALID_PASSWORD
    [InlineData(1326)] // ERROR_LOGON_FAILURE
    public void MapWNetError_AuthCodes_MapToAuthRejected(int code)
    {
        Assert.IsType<MountError.AuthRejected>(WindowsWslNetworkShareMounter.MapWNetError(code, Location));
    }

    [Theory]
    [InlineData(53)] // ERROR_BAD_NETPATH
    [InlineData(67)] // ERROR_BAD_NET_NAME
    [InlineData(1203)] // ERROR_NO_NET_OR_BAD_PATH
    [InlineData(1231)] // ERROR_NETWORK_UNREACHABLE
    [InlineData(4242)] // unknown code
    public void MapWNetError_OtherCodes_MapToFailed(int code)
    {
        var error = Assert.IsType<MountError.Failed>(WindowsWslNetworkShareMounter.MapWNetError(code, Location));
        Assert.NotEmpty(error.Message);
    }

    // --- Linux: GError classification ---

    [Theory]
    [InlineData(GioNative.IoErrorPermissionDenied)]
    [InlineData(GioNative.IoErrorFailedHandled)]
    public void ClassifyGioError_AuthCodes_MapToAuthRejected(int code)
    {
        Assert.IsType<MountError.AuthRejected>(
            LinuxNetworkShareMounter.ClassifyGioError(true, code, "", Location));
    }

    [Theory]
    [InlineData(GioNative.IoErrorNotSupported)]
    [InlineData(GioNative.IoErrorDbusError)]
    public void ClassifyGioError_BackendMissingCodes_MapToGvfsMessage(int code)
    {
        var error = Assert.IsType<MountError.Failed>(
            LinuxNetworkShareMounter.ClassifyGioError(true, code, "", Location));
        Assert.Equal(Strings.Get("Error.SmbGvfsMissing"), error.Message);
    }

    [Theory]
    [InlineData(GioNative.IoErrorTimedOut)]
    [InlineData(GioNative.IoErrorHostNotFound)]
    [InlineData(GioNative.IoErrorHostUnreachable)]
    [InlineData(GioNative.IoErrorNetworkUnreachable)]
    [InlineData(GioNative.IoErrorConnectionRefused)]
    public void ClassifyGioError_NetworkCodes_MapToUnreachable(int code)
    {
        var error = Assert.IsType<MountError.Failed>(
            LinuxNetworkShareMounter.ClassifyGioError(true, code, "", Location));
        Assert.Equal(
            string.Format(Strings.Get("Error.SmbShareUnreachable"), Location.DisplayName), error.Message);
    }

    [Fact]
    public void ClassifyGioError_UnknownOrForeignDomain_MapsToGenericFailureWithMessage()
    {
        var unknown = Assert.IsType<MountError.Failed>(
            LinuxNetworkShareMounter.ClassifyGioError(true, 4242, "backend exploded", Location));
        Assert.Contains("backend exploded", unknown.Message);

        var foreign = Assert.IsType<MountError.Failed>(
            LinuxNetworkShareMounter.ClassifyGioError(false, GioNative.IoErrorTimedOut, "other domain", Location));
        Assert.Contains("other domain", foreign.Message);
    }

    // --- Linux: gvfs mountpoint convention ---

    [Fact]
    public void BuildGvfsMountPath_UsesRuntimeDirectoryAndLowercasesHostAndShare()
    {
        var path = LinuxNetworkShareMounter.BuildGvfsMountPath(
            "/tmp/isolated-runtime", "NAS.Local", "Backups");

        Assert.Equal("/tmp/isolated-runtime/gvfs/smb-share:server=nas.local,share=backups", path);
    }

    [Fact]
    public void TryFindGvfsMountPath_FindsEntryUnderRuntimeDirectory()
    {
        var runtimeDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var mountPoint = Path.Combine(runtimeDirectory, "gvfs", "smb-share:server=nas,share=backups,user=bob");
        Directory.CreateDirectory(mountPoint);

        try
        {
            Assert.Equal(mountPoint, LinuxNetworkShareMounter.TryFindGvfsMountPath(Location, runtimeDirectory));
        }
        finally
        {
            Directory.Delete(runtimeDirectory, true);
        }
    }

    [Theory]
    [InlineData("smb-share:server=nas,share=backups", "nas", "backups", true)]
    [InlineData("smb-share:server=nas,share=backups,user=bob", "NAS", "Backups", true)]
    [InlineData("smb-share:server=nas,share=my%20share", "nas", "my share", true)]
    [InlineData("smb-share:server=other,share=backups", "nas", "backups", false)]
    [InlineData("smb-share:server=nas,share=other", "nas", "backups", false)]
    [InlineData("sftp:host=nas", "nas", "backups", false)]
    public void GvfsEntryMatches_ComparesParsedPairs(string entry, string host, string share, bool expected)
    {
        Assert.Equal(expected, LinuxNetworkShareMounter.GvfsEntryMatches(entry, host, share));
    }

    // --- Windows: WSL mountpoint sanitization ---

    [Fact]
    public void BuildWslMountPoint_SanitizesAndLowercases()
    {
        var path = WindowsWslNetworkShareMounter.BuildWslMountPoint("NAS.local", "My Share");

        Assert.Equal("/mnt/borgmate-smb/nas.local_my_share", path);
    }

    // --- macOS: `mount` output parsing ---

    [Fact]
    public void FindSmbMountPoint_FindsMatchingSmbfsLine()
    {
        const string output = """
            /dev/disk3s1 on / (apfs, sealed, local, read-only, journaled)
            //bob@nas.local/backups on /Volumes/backups (smbfs, nodev, nosuid, mounted by bob)
            """;

        var mountPoint = MacOsNetworkShareMounter.FindSmbMountPoint(output, "nas.local", "backups");

        Assert.Equal("/Volumes/backups", mountPoint);
    }

    [Fact]
    public void FindSmbMountPoint_StripsBonjourSuffix()
    {
        const string output =
            "//bob@nas._smb._tcp.local/backups on /Volumes/backups-1 (smbfs, nodev, nosuid, mounted by bob)";

        var mountPoint = MacOsNetworkShareMounter.FindSmbMountPoint(output, "nas", "backups");

        Assert.Equal("/Volumes/backups-1", mountPoint);
    }

    [Fact]
    public void FindSmbMountPoint_MountPointWithSpaces()
    {
        const string output = "//nas/My%20Share on /Volumes/My Share (smbfs, nodev, nosuid, mounted by bob)";

        var mountPoint = MacOsNetworkShareMounter.FindSmbMountPoint(output, "nas", "My Share");

        Assert.Equal("/Volumes/My Share", mountPoint);
    }

    [Fact]
    public void FindSmbMountPoint_IgnoresOtherHostsAndFileSystems()
    {
        const string output = """
            //bob@other/backups on /Volumes/backups (smbfs, nodev, nosuid, mounted by bob)
            //bob@nas/media on /Volumes/media (smbfs, nodev, nosuid, mounted by bob)
            map auto_home on /System/Volumes/Data/home (autofs, automounted, nobrowse)
            """;

        Assert.Null(MacOsNetworkShareMounter.FindSmbMountPoint(output, "nas", "backups"));
    }
}
