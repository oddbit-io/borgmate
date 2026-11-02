using BorgMate.Infrastructure.Updates;

namespace BorgMate.Tests.Infrastructure;

public class ChangelogParserTests
{
    private const string Changelog = """
        [
          { "version": "1.0.0", "changes": {} },
          { "version": "1.0.0-rc.1", "changes": {} },
          { "version": "1.0.0-beta.11", "changes": {} },
          { "version": "1.0.0-beta.2", "changes": {} },
          { "version": "1.0.0-alpha.1", "changes": {} },
          { "version": "1.0.0-alpha", "changes": {} }
        ]
        """;

    [Fact]
    public void ParseBetween_UsesSemVerPrecedence()
    {
        var entries = ChangelogParser.ParseBetween(Changelog, "1.0.0-beta.2", "1.0.0-rc.1");

        Assert.Equal(["1.0.0-rc.1", "1.0.0-beta.11"], entries.Select(entry => entry.Version).ToArray());
    }

    [Fact]
    public void ParseBetween_IgnoresBuildMetadataForPrecedence()
    {
        var entries = ChangelogParser.ParseBetween(Changelog, "1.0.0-rc.1+first", "1.0.0-rc.1+second");

        Assert.Empty(entries);
    }

    [Fact]
    public void Parse_KeepsEveryEntryInFileOrder()
    {
        var entries = ChangelogParser.Parse(Changelog);

        Assert.Equal(["1.0.0", "1.0.0-rc.1", "1.0.0-beta.11", "1.0.0-beta.2", "1.0.0-alpha.1", "1.0.0-alpha"],
            entries.Select(entry => entry.Version).ToArray());
    }

    [Fact]
    public void ParseBetween_PreservesChangeCategories()
    {
        const string json = """
            [
              {
                "version": "1.0.0-alpha",
                "date": "2026-07-14",
                "changes": {
                  "features": ["Feature"],
                  "improvements": ["Improvement"],
                  "fixes": ["Fix"],
                  "security": ["Security"]
                }
              }
            ]
            """;

        var entry = Assert.Single(ChangelogParser.ParseBetween(json, "0.9.12", "1.0.0-alpha"));

        Assert.Equal(new DateOnly(2026, 7, 14), entry.Date);
        Assert.Equal(["Feature"], entry.Changes.Features);
        Assert.Equal(["Improvement"], entry.Changes.Improvements);
        Assert.Equal(["Fix"], entry.Changes.Fixes);
        Assert.Equal(["Security"], entry.Changes.Security);
    }
}
