using BorgMate.Models;
using BorgMate.Views.Converters;
using System.Globalization;

namespace BorgMate.Tests;

public class FileIconConverterTests
{
    private static string Convert(string name, bool isDir = false) =>
        (string)FileIconConverter.Instance.Convert(
            new ArchiveFileNode { Name = name, FullPath = name, IsDirectory = isDir },
            typeof(string), null, CultureInfo.InvariantCulture);

    [Fact]
    public void Directory_ReturnsFolderFill()
    {
        Assert.Equal("FolderFill", Convert("docs", isDir: true));
    }

    [Theory]
    [InlineData("app.cs", "FileEarmarkCode")]
    [InlineData("script.py", "FileEarmarkCode")]
    [InlineData("index.html", "FileEarmarkCode")]
    [InlineData("style.css", "FileEarmarkCode")]
    [InlineData("config.json", "FileEarmarkCode")]
    [InlineData("build.sh", "FileEarmarkCode")]
    public void CodeFiles_ReturnCodeIcon(string name, string expected)
    {
        Assert.Equal(expected, Convert(name));
    }

    [Theory]
    [InlineData("readme.md", "FileEarmarkText")]
    [InlineData("output.log", "FileEarmarkText")]
    [InlineData("data.csv", "FileEarmarkText")]
    public void TextFiles_ReturnTextIcon(string name, string expected)
    {
        Assert.Equal(expected, Convert(name));
    }

    [Theory]
    [InlineData("photo.jpg", "FileEarmarkImage")]
    [InlineData("logo.png", "FileEarmarkImage")]
    [InlineData("icon.svg", "FileEarmarkImage")]
    [InlineData("photo.heic", "FileEarmarkImage")]
    public void ImageFiles_ReturnImageIcon(string name, string expected)
    {
        Assert.Equal(expected, Convert(name));
    }

    [Theory]
    [InlineData("song.mp3", "FileEarmarkMusic")]
    [InlineData("track.flac", "FileEarmarkMusic")]
    [InlineData("voice.opus", "FileEarmarkMusic")]
    public void AudioFiles_ReturnMusicIcon(string name, string expected)
    {
        Assert.Equal(expected, Convert(name));
    }

    [Theory]
    [InlineData("video.mp4", "FileEarmarkPlay")]
    [InlineData("movie.mkv", "FileEarmarkPlay")]
    [InlineData("clip.3gp", "FileEarmarkPlay")]
    public void VideoFiles_ReturnPlayIcon(string name, string expected)
    {
        Assert.Equal(expected, Convert(name));
    }

    [Theory]
    [InlineData("backup.zip", "FileEarmarkZip")]
    [InlineData("archive.tar", "FileEarmarkZip")]
    [InlineData("image.iso", "FileEarmarkZip")]
    [InlineData("package.deb", "FileEarmarkZip")]
    public void ArchiveFiles_ReturnZipIcon(string name, string expected)
    {
        Assert.Equal(expected, Convert(name));
    }

    [Theory]
    [InlineData("report.pdf", "FileEarmarkPdf")]
    [InlineData("doc.docx", "FileEarmarkWord")]
    [InlineData("sheet.xlsx", "FileEarmarkExcel")]
    [InlineData("slides.pptx", "FileEarmarkPpt")]
    public void OfficeFiles_ReturnCorrectIcon(string name, string expected)
    {
        Assert.Equal(expected, Convert(name));
    }

    [Theory]
    [InlineData("font.ttf", "FileEarmarkFont")]
    [InlineData("app.exe", "FileEarmarkBinary")]
    [InlineData("data.db", "FileEarmarkSpreadsheet")]
    public void SpecialTypes_ReturnCorrectIcon(string name, string expected)
    {
        Assert.Equal(expected, Convert(name));
    }

    [Fact]
    public void UnknownExtension_ReturnsDefault()
    {
        Assert.Equal("FileEarmark", Convert("file.xyz123"));
    }

    [Fact]
    public void NoExtension_ReturnsDefault()
    {
        Assert.Equal("FileEarmark", Convert("Makefile"));
    }

    [Fact]
    public void CaseInsensitive_UppercaseExtension()
    {
        Assert.Equal("FileEarmarkImage", Convert("PHOTO.JPG"));
    }

    [Fact]
    public void NullValue_ReturnsDefault()
    {
        var result = FileIconConverter.Instance.Convert(null, typeof(string), null, CultureInfo.InvariantCulture);
        Assert.Equal("FileEarmark", result);
    }
}
