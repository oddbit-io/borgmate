using BorgMate.Localization;
using BorgMate.Services.Config;

namespace BorgMate.Tests;

[Collection("StringsTests")]
public class StringsFormatTests
{
    private readonly AppSettings _settings = new();

    public StringsFormatTests()
    {
        // Ensure English and binary units for deterministic output
        Strings.SetLanguage("en");
        Strings.Initialize(_settings);
        _settings.BinaryUnits = true;
    }

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(500, "500 B")]
    [InlineData(1024, "1.0 KiB")]
    [InlineData(1536, "1.5 KiB")]
    [InlineData(1_048_576, "1.0 MiB")]
    [InlineData(1_073_741_824, "1.0 GiB")]
    [InlineData(1_099_511_627_776, "1.0 TiB")]
    public void FormatBytes_BinaryUnits(long bytes, string expected)
    {
        _settings.BinaryUnits = true;
        Assert.Equal(expected, Strings.FormatBytes(bytes));
    }

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(500, "500 B")]
    [InlineData(1000, "1.0 KB")]
    [InlineData(1500, "1.5 KB")]
    [InlineData(1_000_000, "1.0 MB")]
    [InlineData(1_000_000_000, "1.0 GB")]
    [InlineData(1_000_000_000_000, "1.0 TB")]
    public void FormatBytes_DecimalUnits(long bytes, string expected)
    {
        _settings.BinaryUnits = false;
        Assert.Equal(expected, Strings.FormatBytes(bytes));
    }

    [Theory]
    [InlineData(100, "100 B/s")]
    [InlineData(1024, "1.0 KiB/s")]
    [InlineData(10_485_760, "10.0 MiB/s")]
    public void FormatSpeed_BinaryUnits(long bytesPerSec, string expected)
    {
        _settings.BinaryUnits = true;
        Assert.Equal(expected, Strings.FormatSpeed(bytesPerSec));
    }

    [Fact]
    public void FormatBytesInUnit_SameUnitAsReference()
    {
        _settings.BinaryUnits = true;
        // Reference is 2 GiB → unit is GiB. Format 512 MiB in GiB.
        var result = Strings.FormatBytesInUnit(536_870_912, 2_147_483_648);
        Assert.Equal("0.5 GiB", result);
    }
}
