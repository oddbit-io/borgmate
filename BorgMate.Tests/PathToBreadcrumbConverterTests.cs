using System.Collections.Generic;
using BorgMate.Views.Converters;
using System.Globalization;

namespace BorgMate.Tests;

public class PathToBreadcrumbConverterTests
{
    private static List<PathSegment> Convert(string? path) =>
        (List<PathSegment>)PathToBreadcrumbConverter.Instance.Convert(path, typeof(object), null, CultureInfo.InvariantCulture);

    [Fact]
    public void UnixPath_SplitsCorrectly()
    {
        var segments = Convert("/home/user/docs");
        Assert.Equal(3, segments.Count);
        Assert.Equal("home", segments[0].Name);
        Assert.True(segments[0].HasSeparator);
        Assert.Equal("user", segments[1].Name);
        Assert.True(segments[1].HasSeparator);
        Assert.Equal("docs", segments[2].Name);
        Assert.False(segments[2].HasSeparator);
    }

    [Fact]
    public void WindowsPath_SplitsCorrectly()
    {
        var segments = Convert(@"C:\Users\foo\Documents");
        Assert.Equal(4, segments.Count);
        Assert.Equal("C:", segments[0].Name);
        Assert.Equal("Documents", segments[3].Name);
        Assert.False(segments[3].HasSeparator);
    }

    [Fact]
    public void SingleSegment_NoSeparator()
    {
        var segments = Convert("filename.txt");
        Assert.Single(segments);
        Assert.Equal("filename.txt", segments[0].Name);
        Assert.False(segments[0].HasSeparator);
    }

    [Fact]
    public void NullOrEmpty_ReturnsEmpty()
    {
        var result = PathToBreadcrumbConverter.Instance.Convert(null, typeof(object), null, CultureInfo.InvariantCulture);
        Assert.Empty((PathSegment[])result);

        result = PathToBreadcrumbConverter.Instance.Convert("", typeof(object), null, CultureInfo.InvariantCulture);
        Assert.Empty((PathSegment[])result);
    }

    [Fact]
    public void TrailingSeparator_IgnoresEmpty()
    {
        var segments = Convert("/home/user/");
        Assert.Equal(2, segments.Count);
        Assert.Equal("home", segments[0].Name);
        Assert.Equal("user", segments[1].Name);
    }
}
