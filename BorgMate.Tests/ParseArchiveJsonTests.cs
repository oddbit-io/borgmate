using System;
using BorgMate.Services.Borg;

namespace BorgMate.Tests;

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
        Assert.Single(archives);
        Assert.Equal("backup-2026-04-01", archives[0].Name);
        Assert.Equal(new DateTime(2026, 4, 1, 3, 0, 0), archives[0].Date);
    }

    [Fact]
    public void NoArchivesProperty_ReturnsEmpty()
    {
        var archives = ArchiveJsonParser.ParseArchiveList("{}");
        Assert.Empty(archives);
    }

    [Fact]
    public void EmptyArchives_ReturnsEmpty()
    {
        var archives = ArchiveJsonParser.ParseArchiveList("""{"archives": []}""");
        Assert.Empty(archives);
    }

    [Fact]
    public void InvalidJson_FallsBackToLineNames()
    {
        var text = "daily-2026-03-27\ndaily-2026-03-26\n";

        var archives = ArchiveJsonParser.ParseArchiveList(text);
        Assert.Equal(2, archives.Count);
        Assert.Equal("daily-2026-03-27", archives[0].Name);
        Assert.Equal(DateTime.MinValue, archives[0].Date);
    }

    [Fact]
    public void InvalidJson_SkipsJsonLines()
    {
        var text = "{invalid\n[broken\nactual-archive-name\n";

        var archives = ArchiveJsonParser.ParseArchiveList(text);
        Assert.Single(archives);
        Assert.Equal("actual-archive-name", archives[0].Name);
    }

    [Fact]
    public void MissingTimeField_UsesMinValue()
    {
        var json = """{"archives": [{"name": "no-date"}]}""";

        var archives = ArchiveJsonParser.ParseArchiveList(json);
        Assert.Single(archives);
        Assert.Equal(DateTime.MinValue, archives[0].Date);
    }
}
