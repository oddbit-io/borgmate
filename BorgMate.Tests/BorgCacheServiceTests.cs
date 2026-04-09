using System.Collections.Generic;
using BorgMate.Models;
using BorgMate.Services;

namespace BorgMate.Tests;

public class BorgCacheServiceTests
{
    // --- Archive List ---

    [Fact]
    public void ArchiveList_GetSet_Roundtrip()
    {
        var svc = new BorgCacheService();
        var list = new List<BorgArchive> { new("a", DateTime.Now) };

        svc.SetArchiveList("/repo", list);

        Assert.Same(list, svc.GetArchiveList("/repo"));
    }

    [Fact]
    public void ArchiveList_Miss_ReturnsNull()
    {
        var svc = new BorgCacheService();
        Assert.Null(svc.GetArchiveList("/nonexistent"));
    }

    [Fact]
    public void ArchiveList_Overwrite()
    {
        var svc = new BorgCacheService();
        svc.SetArchiveList("/repo", [new("old", DateTime.Now)]);
        var newer = new List<BorgArchive> { new("new", DateTime.Now) };

        svc.SetArchiveList("/repo", newer);

        Assert.Same(newer, svc.GetArchiveList("/repo"));
    }

    // --- Archive Detail ---

    [Fact]
    public void ArchiveDetail_GetSet_Roundtrip()
    {
        var svc = new BorgCacheService();
        var detail = new BorgCacheService.ArchiveDetail(1000, 50);

        svc.SetArchiveDetail("/repo", "archive1", detail);

        var result = svc.GetArchiveDetail("/repo", "archive1");
        Assert.NotNull(result);
        Assert.Equal(1000, result.OriginalSize);
        Assert.Equal(50, result.FileCount);
    }

    [Fact]
    public void ArchiveDetail_Miss_ReturnsNull()
    {
        var svc = new BorgCacheService();
        Assert.Null(svc.GetArchiveDetail("/repo", "missing"));
    }

    // --- Archive Contents ---

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

    // --- Diff ---

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

    // --- InvalidateRepo ---

    [Fact]
    public void InvalidateRepo_ClearsAllForRepo()
    {
        var svc = new BorgCacheService();
        svc.SetArchiveList("/repo", [new("a", DateTime.Now)]);
        svc.SetArchiveDetail("/repo", "a", new(100, 5));
        svc.SetArchiveContents("/repo", "a", "json");
        svc.SetDiff("/repo", "a", "b", new() { ["f"] = FileChangeKind.Added });

        svc.InvalidateRepo("/repo");

        Assert.Null(svc.GetArchiveList("/repo"));
        Assert.Null(svc.GetArchiveDetail("/repo", "a"));
        Assert.Null(svc.GetArchiveContents("/repo", "a"));
        Assert.Null(svc.GetDiff("/repo", "a", "b"));
    }

    [Fact]
    public void InvalidateRepo_DoesNotAffectOtherRepos()
    {
        var svc = new BorgCacheService();
        svc.SetArchiveList("/repo1", [new("a", DateTime.Now)]);
        svc.SetArchiveList("/repo2", [new("b", DateTime.Now)]);
        svc.SetArchiveDetail("/repo1", "a", new(100, 5));
        svc.SetArchiveDetail("/repo2", "b", new(200, 10));

        svc.InvalidateRepo("/repo1");

        Assert.Null(svc.GetArchiveList("/repo1"));
        Assert.NotNull(svc.GetArchiveList("/repo2"));
        Assert.Null(svc.GetArchiveDetail("/repo1", "a"));
        Assert.NotNull(svc.GetArchiveDetail("/repo2", "b"));
    }

    // --- ClearAll ---

    [Fact]
    public void ClearAll_RemovesEverything()
    {
        var svc = new BorgCacheService();
        svc.SetArchiveList("/repo1", [new("a", DateTime.Now)]);
        svc.SetArchiveList("/repo2", [new("b", DateTime.Now)]);
        svc.SetArchiveDetail("/repo1", "a", new(100, 5));
        svc.SetArchiveContents("/repo1", "a", "json");
        svc.SetDiff("/repo1", "a", "b", new() { ["f"] = FileChangeKind.Modified });

        svc.ClearAll();

        Assert.Null(svc.GetArchiveList("/repo1"));
        Assert.Null(svc.GetArchiveList("/repo2"));
        Assert.Null(svc.GetArchiveDetail("/repo1", "a"));
        Assert.Null(svc.GetArchiveContents("/repo1", "a"));
        Assert.Null(svc.GetDiff("/repo1", "a", "b"));
    }

    [Fact]
    public void ClearAll_ThenReuse()
    {
        var svc = new BorgCacheService();
        svc.SetArchiveList("/repo", [new("a", DateTime.Now)]);

        svc.ClearAll();
        svc.SetArchiveList("/repo", [new("b", DateTime.Now)]);

        Assert.Equal("b", svc.GetArchiveList("/repo")![0].Name);
    }
}
