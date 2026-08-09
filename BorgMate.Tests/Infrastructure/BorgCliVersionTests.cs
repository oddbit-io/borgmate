using BorgMate.Infrastructure.Borg;

namespace BorgMate.Tests.Infrastructure;

public class BorgCliVersionTests
{
    [Fact]
    public void Parse_Borg14Version_MatchesBorg1()
    {
        var version = BorgCliVersion.Parse("borg 1.4.4");

        Assert.NotNull(version);
        Assert.Equal(1, version.Major);
        Assert.Equal(4, version.Minor);
        Assert.Equal(4, version.Patch);
    }

    [Fact]
    public void Parse_Borg20Beta21_IsSupported()
    {
        var version = BorgCliVersion.Parse("borg 2.0.0b21");

        Assert.NotNull(version);
        Assert.Equal(2, version.Major);
        Assert.Equal(21, version.Beta);
        Assert.True(version.IsSupportedBorg2);
    }

    [Theory]
    [InlineData("borg 2.0.0b20")]
    [InlineData("borg 2.0.0b21.dev437")]
    [InlineData("borg 2.0.0b22")]
    [InlineData("borg 2.0.0")]
    public void Parse_OtherBorg2Versions_AreNotSupported(string versionText)
    {
        var version = BorgCliVersion.Parse(versionText);

        Assert.NotNull(version);
        Assert.False(version.IsSupportedBorg2);
    }

    [Fact]
    public void Parse_InvalidText_ReturnsNull()
    {
        Assert.Null(BorgCliVersion.Parse("not borg"));
    }
}
