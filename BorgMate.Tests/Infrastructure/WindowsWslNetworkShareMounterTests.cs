using BorgMate.Infrastructure.Platform;

namespace BorgMate.Tests.Infrastructure;

public class WindowsWslNetworkShareMounterTests
{
    [Fact]
    public void BuildStorageQueryArguments_PreservesMountedPathAsOneArgument()
    {
        var arguments = WindowsWslNetworkShareMounter.BuildStorageQueryArguments(
            "/mnt/borgmate/nas/share/repo with 'quotes';$(unsafe)");

        Assert.Equal(
            ["--", "df", "-P", "-k", "/mnt/borgmate/nas/share/repo with 'quotes';$(unsafe)"],
            arguments);
    }

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
