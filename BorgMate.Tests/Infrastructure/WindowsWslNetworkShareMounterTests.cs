using BorgMate.Domain.Repositories;
using BorgMate.Infrastructure.Platform;

namespace BorgMate.Tests.Infrastructure;

public class WindowsWslNetworkShareMounterTests
{
    private const string UntrustedMountPoint = "/mnt/it's \"q\" $(touch MARKER)";

    [Fact]
    public void BuildStorageQueryArguments_PreservesMountedPathAsOneArgument()
    {
        var arguments = WindowsWslNetworkShareMounter.BuildStorageQueryArguments(
            "/mnt/borgmate/nas/share/repo with 'quotes';$(unsafe)");

        Assert.Equal(
            ["--exec", "df", "-P", "-k", "/mnt/borgmate/nas/share/repo with 'quotes';$(unsafe)"],
            arguments);
    }

    [Fact]
    public void BuildMountPointCheckArguments_ExecsMountpointWithoutDefaultShell()
    {
        Assert.Equal(
            ["--exec", "mountpoint", "-q", "/mnt/$(unsafe)"],
            WindowsWslNetworkShareMounter.BuildMountPointCheckArguments("/mnt/$(unsafe)"));
    }

    [Fact]
    public void BuildDrvfsMountArguments_ScriptContainsNoUntrustedValues()
    {
        var arguments = WindowsWslNetworkShareMounter.BuildDrvfsMountArguments(@"\\h\s'$(x)", UntrustedMountPoint);

        Assert.Equal(["-u", "root", "--exec", "bash", "-c"], arguments.Take(5));
        Assert.Equal(["bash", @"\\h\s'$(x)", UntrustedMountPoint], arguments.Skip(6));
        Assert.DoesNotContain("$(x)", arguments[5]);
        Assert.DoesNotContain("MARKER", arguments[5]);
    }

    [Fact]
    public void BuildCifsMountArguments_ScriptContainsNoUntrustedValues()
    {
        var arguments = WindowsWslNetworkShareMounter.BuildCifsMountArguments(
            "//h/s'$(x)", UntrustedMountPoint, "username=u$(y),noperm");

        Assert.Equal(["-u", "root", "--exec", "bash", "-c"], arguments.Take(5));
        Assert.Equal(["bash", "//h/s'$(x)", UntrustedMountPoint, "username=u$(y),noperm"], arguments.Skip(6));
        Assert.DoesNotContain("$(x)", arguments[5]);
        Assert.DoesNotContain("$(y)", arguments[5]);
    }

    [SkippableFact]
    public async Task BuildDrvfsMountArguments_ShellMetacharactersReachMountVerbatimAndAreNotExecuted()
    {
        BashHarness.RequireBash();

        using var stubs = new MountStubs();
        var marker = stubs.RootPath + "/marker";
        var mountPoint = UntrustedMountPoint.Replace("MARKER", marker);
        // The msys runtime on Windows collapses a leading double backslash and re-parses single quotes.
        var unc = $@"h\s$(touch {marker}2)`touch {marker}3`";

        var result = await BashHarness.RunWslArgumentsAsync(
            WindowsWslNetworkShareMounter.BuildDrvfsMountArguments(unc, mountPoint), stubs.EnvironmentVariables);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(["mkdir -p -- " + mountPoint, "mount -t drvfs " + unc + " " + mountPoint], stubs.ReadLog());
        Assert.False(File.Exists(marker));
        Assert.False(File.Exists(marker + "2"));
        Assert.False(File.Exists(marker + "3"));
    }

    [SkippableFact]
    public async Task BuildCifsMountArguments_ShellMetacharactersReachMountVerbatimAndAreNotExecuted()
    {
        BashHarness.RequireBash();

        using var stubs = new MountStubs();
        var marker = stubs.RootPath + "/marker";
        var mountPoint = UntrustedMountPoint.Replace("MARKER", marker);
        var source = $"//h/s'$(touch {marker}2)";
        var options = $"username=u`touch {marker}3`,noperm";

        var result = await BashHarness.RunWslArgumentsAsync(
            WindowsWslNetworkShareMounter.BuildCifsMountArguments(source, mountPoint, options),
            stubs.EnvironmentVariables);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(["mkdir -p -- " + mountPoint, $"mount -t cifs {source} {mountPoint} -o {options}"],
            stubs.ReadLog());
        Assert.False(File.Exists(marker));
        Assert.False(File.Exists(marker + "2"));
        Assert.False(File.Exists(marker + "3"));
    }

    [Fact]
    public void BuildCifsOptions_UsernameWithComma_IsRejectedBeforeReachingOptions()
    {
        var location = new SmbLocation { Host = "nas", Share = "s", Username = "u,uid=0,sec=none" };

        var result = WindowsWslNetworkShareMounter.BuildCifsOptions(location, true, Owner);

        Assert.True(result.IsT1);
        Assert.IsType<MountError.Failed>(result.AsT1);
    }

    [Fact]
    public void BuildCifsOptions_PlainUsername_BecomesOneUsernameOption()
    {
        var location = new SmbLocation { Host = "nas", Share = "s", Username = "DOM\\user=1" };

        Assert.Equal("username=DOM\\user=1,uid=1000,gid=1001,file_mode=0600,dir_mode=0700,iocharset=utf8",
            WindowsWslNetworkShareMounter.BuildCifsOptions(location, true, Owner).AsT0);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BuildCifsOptions_GivesTheShareOnlyToTheBorgUser(bool authenticated)
    {
        var location = new SmbLocation { Host = "nas", Share = "s", Username = "user" };

        var options = WindowsWslNetworkShareMounter.BuildCifsOptions(location, authenticated, Owner).AsT0.Split(',');

        Assert.DoesNotContain("noperm", options);
        Assert.Contains("uid=1000", options);
        Assert.Contains("file_mode=0600", options);
        Assert.Contains("dir_mode=0700", options);
    }

    [Theory]
    [InlineData("1000:1001\n", 1000, 1001)]
    [InlineData("0:0", 0, 0)]
    public void WslUserIds_ParsesIdOutput(string output, int uid, int gid)
    {
        var ids = WslUserIds.Parse(output);

        Assert.NotNull(ids);
        Assert.Equal(uid, ids.Uid);
        Assert.Equal(gid, ids.Gid);
    }

    private static readonly WslUserIds Owner = new() { Uid = 1000, Gid = 1001 };

    [Fact]
    public void DiskSpaceParser_ParsesWindowsWslResultWithoutStartingWsl()
    {
        const string output = "Filesystem 1024-blocks Used Available Capacity Mounted on\n" +
                              "/dev/sda 200 125 75 63% /mnt/borgmate\n";

        var snapshot = DiskSpaceProbe.ParseDfOutput(output);

        Assert.NotNull(snapshot);
        Assert.Equal(200 * 1024, snapshot.CapacityBytes);
        Assert.Equal(75 * 1024, snapshot.AvailableBytes);
    }
}
