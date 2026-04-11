using System.Collections.Generic;
using BorgMate.Models;
using BorgMate.Services;

namespace BorgMate.Tests;

public class BorgCacheServiceTests
{
    // Archive list lives on BorgRepository.LoadedArchives.
    // Archive detail lives on BorgArchive.OriginalSize/FileCount.
    // This cache only holds contents and diffs.

    [Fact]
    public void ArchiveContents_GetSet_Roundtrip()
    {
        var svc = new BorgCacheService();
        svc.SetArchiveContents("/repo", "archive1", "{json}");
        Assert.Equal("{json}", svc.GetArchiveContents("/repo", "archive1"));
    }

    [Fact]
    public void ArchiveContents_Miss_ReturnsNull()
    {
        var svc = new BorgCacheService();
        Assert.Null(svc.GetArchiveContents("/repo", "missing"));
    }

    [Fact]
    public void Diff_GetSet_Roundtrip()
    {
        var svc = new BorgCacheService();
        var diff = new Dictionary<string, FileChangeKind> { ["file.txt"] = FileChangeKind.Added };
        svc.SetDiff("/repo", "a1", "a2", diff);
        Assert.Same(diff, svc.GetDiff("/repo", "a1", "a2"));
    }

    [Fact]
    public void Diff_Miss_ReturnsNull()
    {
        var svc = new BorgCacheService();
        Assert.Null(svc.GetDiff("/repo", "a1", "a2"));
    }

    [Fact]
    public void InvalidateRepo_ClearsAllForRepo()
    {
        var svc = new BorgCacheService();
        svc.SetArchiveContents("/repo", "a", "json");
        svc.SetDiff("/repo", "a", "b", new() { ["f"] = FileChangeKind.Added });

        svc.InvalidateRepo("/repo");

        Assert.Null(svc.GetArchiveContents("/repo", "a"));
        Assert.Null(svc.GetDiff("/repo", "a", "b"));
    }

    [Fact]
    public void ClearAll_RemovesEverything()
    {
        var svc = new BorgCacheService();
        svc.SetArchiveContents("/repo", "a", "json");
        svc.SetDiff("/repo", "a", "b", new() { ["f"] = FileChangeKind.Modified });

        svc.ClearAll();

        Assert.Null(svc.GetArchiveContents("/repo", "a"));
        Assert.Null(svc.GetDiff("/repo", "a", "b"));
    }
}
