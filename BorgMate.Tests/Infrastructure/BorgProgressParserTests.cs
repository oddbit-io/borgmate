using Borg1ProgressParser = BorgMate.Infrastructure.Borg.Borg1ProgressParser;
using Borg2ProgressParser = BorgMate.Infrastructure.Borg.Borg2ProgressParser;
using BorgProgressParser = BorgMate.Infrastructure.Borg.BorgProgressParser;

namespace BorgMate.Tests.Infrastructure;

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

    [Fact]
    public void TryParseCreateProgress_Borg1Counters()
    {
        var parsed = Borg1ProgressParser.TryParseCreateProgress(
            "1.23 GB O  456 MB C  123 MB D  789 N /home/user/file",
            out var processedBytes,
            out var fileCount);

        Assert.True(parsed);
        Assert.Equal(1_230_000_000L, processedBytes);
        Assert.Equal(789, fileCount);
    }

    [Fact]
    public void TryParseCreateProgress_Borg2UploadCounters()
    {
        var parsed = Borg2ProgressParser.TryParseCreateProgress(
            "1.23 GB O  456 MB U  789 N /home/user/file",
            out var processedBytes,
            out var fileCount);

        Assert.True(parsed);
        Assert.Equal(1_230_000_000L, processedBytes);
        Assert.Equal(789, fileCount);
    }

    [Fact]
    public void TryParseCreateProgress_InvalidLine_ReturnsFalse()
    {
        var borg1Parsed = Borg1ProgressParser.TryParseCreateProgress("no progress here", out _, out _);
        var borg2Parsed = Borg2ProgressParser.TryParseCreateProgress("no progress here", out _, out _);

        Assert.False(borg1Parsed);
        Assert.False(borg2Parsed);
    }
}
