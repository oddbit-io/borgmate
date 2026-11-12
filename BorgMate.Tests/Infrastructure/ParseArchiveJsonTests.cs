using BorgMate.Infrastructure.Borg;

namespace BorgMate.Tests.Infrastructure;

public class ParseArchiveJsonTests
{
    [Fact]
    public void Borg1_TimeField()
    {
        var json = """
        {
            "archives": [
                {"name": "daily-2026-03-27", "time": "2026-03-27T14:00:00"},
                {"name": "daily-2026-03-26", "time": "2026-03-26T14:00:00"}
            ]
        }
        """;

        var archives = ArchiveJsonParser.ParseArchiveList(json);
        Assert.NotNull(archives);
        Assert.Equal(2, archives.Count);
        Assert.Equal("daily-2026-03-27", archives[0].Name);
        Assert.Equal(new DateTime(2026, 3, 27, 14, 0, 0), archives[0].Date);
    }

    [Fact]
    public void Borg2_StartField()
    {
        var json = """
        {
            "archives": [
                {"name": "backup-2026-04-01", "start": "2026-04-01T03:00:00"}
            ]
        }
        """;

        var archives = ArchiveJsonParser.ParseArchiveList(json);
        Assert.NotNull(archives);
        Assert.Single(archives);
        Assert.Equal("backup-2026-04-01", archives[0].Name);
        Assert.Equal(new DateTime(2026, 4, 1, 3, 0, 0), archives[0].Date);
    }

    [Fact]
    public void NoArchivesProperty_ReturnsEmpty()
    {
        var archives = ArchiveJsonParser.ParseArchiveList("{}");
        Assert.NotNull(archives);
        Assert.Empty(archives);
    }

    [Fact]
    public void EmptyArchives_ReturnsEmpty()
    {
        var archives = ArchiveJsonParser.ParseArchiveList("""{"archives": []}""");
        Assert.NotNull(archives);
        Assert.Empty(archives);
    }

    [Theory]
    [InlineData("daily-2026-03-27\ndaily-2026-03-26\n")]
    [InlineData("{invalid\n[broken\nactual-archive-name\n")]
    public void InvalidJson_YieldsNoArchiveNames(string text)
    {
        Assert.Null(ArchiveJsonParser.ParseArchiveList(text));
    }

    [Fact]
    public void EntryWithoutStringName_IsSkipped()
    {
        var json = """{"archives": [{"time": "2026-03-27T10:00:00"}, {"name": 5}, {"name": "kept"}]}""";

        var archives = ArchiveJsonParser.ParseArchiveList(json);

        Assert.NotNull(archives);
        var archive = Assert.Single(archives);
        Assert.Equal("kept", archive.Name);
    }

    [Fact]
    public void MissingTimeField_UsesMinValue()
    {
        var json = """{"archives": [{"name": "no-date"}]}""";

        var archives = ArchiveJsonParser.ParseArchiveList(json);
        Assert.NotNull(archives);
        Assert.Single(archives);
        Assert.Equal(DateTime.MinValue, archives[0].Date);
    }

    [Theory]
    [InlineData("none")]
    [InlineData("repokey-blake2")]
    public void ParseEncryptionMode_ReturnsMode(string mode)
    {
        var json = $$"""
        {
            "encryption": {"mode": "{{mode}}"},
            "repository": {"id": "abc", "location": "/repo"},
            "security_dir": "/home/user/.config/borg/security/abc"
        }
        """;

        Assert.Equal(mode, ArchiveJsonParser.ParseEncryptionMode(json));
    }

    [Fact]
    public void ParseEncryptionMode_MissingProperty_ReturnsNull()
    {
        Assert.Null(ArchiveJsonParser.ParseEncryptionMode("""{"repository": {"id": "abc"}}"""));
    }

    [Fact]
    public void ParseEncryptionMode_InvalidJson_ReturnsNull()
    {
        Assert.Null(ArchiveJsonParser.ParseEncryptionMode("not json"));
    }
}
