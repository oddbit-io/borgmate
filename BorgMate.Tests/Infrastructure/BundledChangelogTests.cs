using System.Runtime.CompilerServices;
using BorgMate.Infrastructure.Updates;

namespace BorgMate.Tests.Infrastructure;

public class BundledChangelogTests
{
    [Fact]
    public void Load_ReturnsTheWholeRepositoryChangelog()
    {
        var expected = ChangelogParser.Parse(File.ReadAllText(RepositoryChangelogPath()));

        var entries = BundledChangelog.Load();

        Assert.Equal(expected.Select(entry => entry.Version), entries.Select(entry => entry.Version));
    }

    // The test source sits in BorgMate.Tests/Infrastructure; the changelog is at the repository root.
    private static string RepositoryChangelogPath([CallerFilePath] string testFile = "")
    {
        return Path.GetFullPath(Path.Combine(testFile, "..", "..", "..", "changelog-v2.json"));
    }
}
