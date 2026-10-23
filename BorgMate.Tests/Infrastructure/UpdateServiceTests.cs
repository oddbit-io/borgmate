using BorgMate.Infrastructure.Updates;

namespace BorgMate.Tests.Infrastructure;

public class UpdateServiceTests
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
    public void ParseChangelog_UsesSemVerPrecedence()
    {
        var entries = UpdateService.ParseChangelog(Changelog, "1.0.0-beta.2", "1.0.0-rc.1");

        Assert.Equal(["1.0.0-rc.1", "1.0.0-beta.11"], entries.Select(entry => entry.Version).ToArray());
    }

    [Fact]
    public void ParseChangelog_IgnoresBuildMetadataForPrecedence()
    {
        var entries = UpdateService.ParseChangelog(Changelog, "1.0.0-rc.1+first", "1.0.0-rc.1+second");

        Assert.Empty(entries);
    }

    [Fact]
    public void ParseChangelog_PreservesChangeCategories()
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

        var entry = Assert.Single(UpdateService.ParseChangelog(json, "0.9.12", "1.0.0-alpha"));

        Assert.Equal(new DateOnly(2026, 7, 14), entry.Date);
        Assert.Equal(["Feature"], entry.Changes.Features);
        Assert.Equal(["Improvement"], entry.Changes.Improvements);
        Assert.Equal(["Fix"], entry.Changes.Fixes);
        Assert.Equal(["Security"], entry.Changes.Security);
    }
}
