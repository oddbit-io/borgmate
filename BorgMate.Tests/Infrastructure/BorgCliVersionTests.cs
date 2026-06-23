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
    public void Parse_Borg20Beta21_IsSupportedWithoutSplitEncryption()
    {
        var version = BorgCliVersion.Parse("borg 2.0.0b21");

        Assert.NotNull(version);
        Assert.Equal(2, version.Major);
        Assert.Equal(21, version.Beta);
        Assert.True(version.IsSupportedBorg2);
        Assert.False(version.UsesBorg2SplitEncryption);
    }

    [Fact]
    public void Parse_OlderBorg2Beta_IsNotSupported()
    {
        var version = BorgCliVersion.Parse("borg 2.0.0b20");

        Assert.NotNull(version);
        Assert.False(version.IsSupportedBorg2);
    }

    [Fact]
    public void Parse_Borg20Beta22Dev_UsesSplitEncryption()
    {
        var version = BorgCliVersion.Parse("borg 2.0.0b22.dev437");

        Assert.NotNull(version);
        Assert.True(version.IsDevelopment);
        Assert.True(version.IsSupportedBorg2);
        Assert.True(version.UsesBorg2SplitEncryption);
    }

    [Fact]
    public void Parse_InvalidText_ReturnsNull()
    {
        Assert.Null(BorgCliVersion.Parse("not borg"));
    }
}
