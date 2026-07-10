using BorgMate.Infrastructure.Updates;

namespace BorgMate.Tests.Infrastructure;

public class AppBuildInfoTests
{
    [Theory]
    [InlineData("1.0.0", "1.0.0")]
    [InlineData("1.0.0-alpha.1", "1.0.0-alpha.1")]
    [InlineData("1.0.0-beta+exp.sha.5114f85", "1.0.0-beta")]
    [InlineData("1.0.0-beta+exp.sha.5114f85.dev.c7cae47.dirty", "1.0.0-beta")]
    public void Constructor_SeparatesDisplayAndComparableVersions(string displayVersion, string currentVersion)
    {
        var info = new AppBuildInfo(displayVersion);

        Assert.Equal(displayVersion, info.DisplayVersion);
        Assert.Equal(currentVersion, info.CurrentVersion);
    }

    [Theory]
    [InlineData("1.0")]
    [InlineData("01.0.0")]
    [InlineData("1.0.0-alpha..1")]
    [InlineData("1.0.0-alpha_1")]
    public void Constructor_RejectsInvalidVersion(string version)
    {
        Assert.Throws<InvalidOperationException>(() => new AppBuildInfo(version));
    }
}
