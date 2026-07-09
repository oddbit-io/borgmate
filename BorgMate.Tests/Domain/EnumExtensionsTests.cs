using BorgMate.Domain.Repositories;
using BorgMate.Domain.Settings;

namespace BorgMate.Tests.Domain;

public class EnumExtensionsTests
{
    [Theory]
    [InlineData(BorgEncryptionMode.None, "none")]
    [InlineData(BorgEncryptionMode.Repokey, "repokey")]
    [InlineData(BorgEncryptionMode.RepokeyBlake2, "repokey-blake2")]
    [InlineData(BorgEncryptionMode.Keyfile, "keyfile")]
    [InlineData(BorgEncryptionMode.KeyfileBlake2, "keyfile-blake2")]
    [InlineData(BorgEncryptionMode.Authenticated, "authenticated")]
    [InlineData(BorgEncryptionMode.AuthenticatedBlake2, "authenticated-blake2")]
    public void ToBorgString_ReturnsCorrectValue(BorgEncryptionMode mode, string expected)
    {
        Assert.Equal(expected, mode.ToBorgString());
    }

    [Fact]
    public void ToBorgString_InvalidValue_ReturnsFallback()
    {
        var invalid = (BorgEncryptionMode)999;
        Assert.Equal("repokey-blake2", invalid.ToBorgString());
    }

    [Fact]
    public void BorgEncryptionModeFromBorgString_RoundTripsAllValues()
    {
        foreach (var mode in Enum.GetValues<BorgEncryptionMode>())
            Assert.Equal(mode, EnumExtensions.BorgEncryptionModeFromBorgString(mode.ToBorgString()));
    }

    [Fact]
    public void Borg2EncryptionModeFromBorgString_RoundTripsAllValues()
    {
        foreach (var mode in Enum.GetValues<Borg2EncryptionMode>())
            Assert.Equal(mode, EnumExtensions.Borg2EncryptionModeFromBorgString(mode.ToBorgString()));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("future-mode")]
    public void EncryptionModeFromBorgString_UnknownString_ReturnsNull(string? mode)
    {
        Assert.Null(EnumExtensions.BorgEncryptionModeFromBorgString(mode));
        Assert.Null(EnumExtensions.Borg2EncryptionModeFromBorgString(mode));
    }
}
