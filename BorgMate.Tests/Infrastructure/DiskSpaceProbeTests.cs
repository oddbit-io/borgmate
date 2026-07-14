using System.IO;
using BorgMate.Infrastructure.Platform;
using BorgMate.Presentation.ViewModels;

namespace BorgMate.Tests.Infrastructure;

public class DiskSpaceProbeTests
{
    [Fact]
    public void GetVolumeSpace_ReturnsPositiveFreeAndTotal_ForExistingPath()
    {
        var space = DiskSpaceProbe.GetVolumeSpace(Path.GetTempPath());

        Assert.True(space.CapacityBytes > 0);
        Assert.True(space.AvailableBytes >= 0);
        Assert.True(space.AvailableBytes <= space.CapacityBytes);
    }

    [Fact]
    public void GetVolumeSpace_Throws_ForMissingPath()
    {
        Assert.Throws<DirectoryNotFoundException>(
            () => DiskSpaceProbe.GetVolumeSpace("/nonexistent/borgmate/probe/path"));
    }

    [Fact]
    public void ParseDfOutput_ParsesPosixKilobyteBlocks()
    {
        const string output = "Filesystem     1024-blocks      Used Available Capacity Mounted on\n" +
                              "/dev/sda1        103180800   6410536  91487560       7% /srv\n";

        var space = DiskSpaceProbe.ParseDfOutput(output);

        Assert.NotNull(space);
        Assert.Equal(103180800L * 1024, space.CapacityBytes);
        Assert.Equal(91487560L * 1024, space.AvailableBytes);
    }

    [Fact]
    public void ParseDfOutput_ReturnsNull_ForUnrecognizedOutput()
    {
        Assert.Null(DiskSpaceProbe.ParseDfOutput(""));
        Assert.Null(DiskSpaceProbe.ParseDfOutput("df: /missing: No such file or directory"));
    }

    [Theory]
    [InlineData("/dev/sda 100 20 101 20% /repo")]
    [InlineData("/dev/sda 100 20 -1 20% /repo")]
    [InlineData("/dev/sda -1 20 10 20% /repo")]
    [InlineData("/dev/sda 100 -1 10 20% /repo")]
    [InlineData("/dev/sda 100 101 10 20% /repo")]
    [InlineData("/dev/sda 9007199254740992 20 10 20% /repo")]
    public void ParseDfOutput_ReturnsNull_ForInvalidCapacity(string dataLine)
    {
        Assert.Null(DiskSpaceProbe.ParseDfOutput($"Filesystem 1024-blocks Used Available Capacity Mounted on\n{dataLine}\n"));
    }
}
