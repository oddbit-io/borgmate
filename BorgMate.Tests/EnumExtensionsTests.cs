using BorgMate.Models;

namespace BorgMate.Tests;

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
}
