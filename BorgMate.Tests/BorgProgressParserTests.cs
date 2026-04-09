using BorgMate.Services.Borg;

namespace BorgMate.Tests;

public class BorgProgressParserTests
{
    [Theory]
    [InlineData("1.23 GB", 1_230_000_000L)]
    [InlineData("456 MB", 456_000_000L)]
    [InlineData("789 kB", 789_000L)]
    [InlineData("100 B", 100L)]
    [InlineData("1.5 TB", 1_500_000_000_000L)]
    [InlineData("2.0 GiB", 2_147_483_648L)]
    [InlineData("1.0 MiB", 1_048_576L)]
    [InlineData("1.0 KiB", 1024L)]
    [InlineData("1.0 TiB", 1_099_511_627_776L)]
    public void ParseBorgSize_ValidUnits(string input, long expected)
    {
        Assert.Equal(expected, BorgProgressParser.ParseBorgSize(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData("abc MB")]
    [InlineData("1.0")]
    [InlineData("1.0 XB")]
    public void ParseBorgSize_InvalidInput_ReturnsZero(string input)
    {
        Assert.Equal(0L, BorgProgressParser.ParseBorgSize(input));
    }

    [Fact]
    public void ParseBorgSize_ZeroBytes()
    {
        Assert.Equal(0L, BorgProgressParser.ParseBorgSize("0 B"));
    }

    [Theory]
    [InlineData("12.34 GB", 12_340_000_000L)]
    [InlineData("0.5 GB", 500_000_000L)]
    [InlineData("999 B", 999L)]
    public void ParseBorgSize_AdditionalValues(string input, long expected)
    {
        Assert.Equal(expected, BorgProgressParser.ParseBorgSize(input));
    }

    [Fact]
    public void ParseBorgSize_NullLike_ReturnsZero()
    {
        Assert.Equal(0L, BorgProgressParser.ParseBorgSize("   "));
    }

    [Theory]
    [InlineData("100 KB", 100_000L)]
    [InlineData("100 KiB", 102_400L)]
    public void ParseBorgSize_KiloByte_BinaryVsDecimal(string input, long expected)
    {
        Assert.Equal(expected, BorgProgressParser.ParseBorgSize(input));
    }
}
