using BorgMate.Localization;
using BorgMate.Services.Config;

namespace BorgMate.Tests;

/// <summary>
/// Tests for Strings localization helpers. Shares a single AppSettings instance
/// to avoid races with StringsFormatTests (xUnit runs classes in parallel).
/// Must not run in parallel with StringsFormatTests — use same collection.
/// </summary>
[Collection("StringsTests")]
public class StringsLocalizationTests
{
    private static readonly AppSettings Settings = new();

    public StringsLocalizationTests()
    {
        Strings.SetLanguage("en");
        Strings.Initialize(Settings);
        Settings.BinaryUnits = true;
    }

    [Theory]
    [InlineData("English", "en")]
    [InlineData("Русский", "ru")]
    [InlineData("Unknown", "en")]
    public void DisplayToCode_MapsCorrectly(string display, string expected)
    {
        Assert.Equal(expected, Strings.DisplayToCode(display));
    }

    [Theory]
    [InlineData("en", "English")]
    [InlineData("ru", "Русский")]
    [InlineData("fr", "Auto")]
    [InlineData("auto", "Auto")]
    public void CodeToDisplay_MapsCorrectly(string code, string expected)
    {
        Assert.Equal(expected, Strings.CodeToDisplay(code));
    }

    [Fact]
    public void Get_ExistingKey_ReturnsValue()
    {
        Assert.NotEmpty(Strings.Get("AppTitle"));
    }

    [Fact]
    public void Get_MissingKey_ReturnsKey()
    {
        Assert.Equal("NonExistentKey123", Strings.Get("NonExistentKey123"));
    }

    [Theory]
    [InlineData(100, "100 B/s")]
    [InlineData(1024, "1.0 KiB/s")]
    [InlineData(1_048_576, "1.0 MiB/s")]
    [InlineData(10_485_760, "10.0 MiB/s")]
    public void FormatSpeed_BinaryUnits(long bytesPerSec, string expected)
    {
        Settings.BinaryUnits = true;
        Assert.Equal(expected, Strings.FormatSpeed(bytesPerSec));
    }

    [Theory]
    [InlineData(100, "100 B/s")]
    [InlineData(1000, "1.0 KB/s")]
    [InlineData(1_000_000, "1.0 MB/s")]
    public void FormatSpeed_DecimalUnits(long bytesPerSec, string expected)
    {
        Settings.BinaryUnits = false;
        Assert.Equal(expected, Strings.FormatSpeed(bytesPerSec));
    }

    [Fact]
    public void FormatBytesInUnit_MatchesReferenceUnit()
    {
        Settings.BinaryUnits = true;
        // 512 MiB formatted in the unit of 2 GiB → should be "0.5 GiB"
        var result = Strings.FormatBytesInUnit(536_870_912, 2_147_483_648);
        Assert.Equal("0.5 GiB", result);
    }

    [Fact]
    public void FormatBytesInUnit_SmallInLargeUnit()
    {
        Settings.BinaryUnits = true;
        // 1 byte formatted in GiB unit
        var result = Strings.FormatBytesInUnit(1, 2_147_483_648);
        Assert.Equal("0.0 GiB", result);
    }
}
