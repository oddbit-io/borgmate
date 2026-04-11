using System.Collections.Generic;
using BorgMate.Models;
using BorgMate.Services;
using BorgMate.Services.Borg;
using BorgMate.Services.Queue;

namespace BorgMate.Tests;

public class RepositoryStoreTests : IDisposable
{
    private readonly JobQueueService _jobQueue = new();
    private readonly RepositoryStore _store;

    public RepositoryStoreTests()
    {
        _store = new RepositoryStore(_jobQueue);
    }

    public void Dispose()
    {
        _store.Dispose();
        _jobQueue.Dispose();
    }

    // --- Collection mutation ---

    [Fact]
    public void Add_AppendsToRepositories()
    {
        var repo = new BorgRepository { Name = "r", Path = "/r" };
        _store.Add(repo);

        Assert.Single(_store.Repositories);
        Assert.Same(repo, _store.Repositories[0]);
    }

    [Fact]
    public void Remove_RemovesFromRepositories()
    {
        var repo = new BorgRepository { Name = "r", Path = "/r" };
        _store.Add(repo);
        _store.Remove(repo);

        Assert.Empty(_store.Repositories);
    }

    [Fact]
    public void Remove_ClearsSelectionWhenRemovingSelectedRepo()
    {
        var repo = new BorgRepository { Name = "r", Path = "/r" };
        _store.Add(repo);
        _store.SelectedRepository = repo;

        _store.Remove(repo);

        Assert.Null(_store.SelectedRepository);
    }

    [Fact]
    public void Remove_PreservesSelectionWhenRemovingDifferentRepo()
    {
        var repoA = new BorgRepository { Name = "a", Path = "/a" };
        var repoB = new BorgRepository { Name = "b", Path = "/b" };
        _store.Add(repoA);
        _store.Add(repoB);
        _store.SelectedRepository = repoA;

        _store.Remove(repoB);

        Assert.Same(repoA, _store.SelectedRepository);
    }

    [Fact]
    public void FindByPath_ReturnsMatchingRepo()
    {
        var repoA = new BorgRepository { Name = "a", Path = "/a" };
        var repoB = new BorgRepository { Name = "b", Path = "/b" };
        _store.Add(repoA);
        _store.Add(repoB);

        Assert.Same(repoB, _store.FindByPath("/b"));
    }

    [Fact]
    public void FindByPath_ReturnsNullWhenNotFound()
    {
        _store.Add(new BorgRepository { Name = "a", Path = "/a" });

        Assert.Null(_store.FindByPath("/missing"));
    }

    // --- SelectionChanged event ---

    [Fact]
    public void SelectionChanged_FiresWithOldAndNewValues()
    {
        var repoA = new BorgRepository { Name = "a", Path = "/a" };
        var repoB = new BorgRepository { Name = "b", Path = "/b" };
        _store.Add(repoA);
        _store.Add(repoB);

        BorgRepository? capturedOld = null;
        BorgRepository? capturedNew = null;
        var fireCount = 0;
        _store.SelectionChanged += (oldRepo, newRepo) =>
        {
            capturedOld = oldRepo;
            capturedNew = newRepo;
            fireCount++;
        };

        _store.SelectedRepository = repoA;
        Assert.Equal(1, fireCount);
        Assert.Null(capturedOld);
        Assert.Same(repoA, capturedNew);

        _store.SelectedRepository = repoB;
        Assert.Equal(2, fireCount);
        Assert.Same(repoA, capturedOld);
        Assert.Same(repoB, capturedNew);

        _store.SelectedRepository = null;
        Assert.Equal(3, fireCount);
        Assert.Same(repoB, capturedOld);
        Assert.Null(capturedNew);
    }

    [Fact]
    public void SelectionChanged_DoesNotFireWhenSettingSameValue()
    {
        var repo = new BorgRepository { Name = "r", Path = "/r" };
        _store.Add(repo);
        _store.SelectedRepository = repo;

        var fireCount = 0;
        _store.SelectionChanged += (_, _) => fireCount++;

        _store.SelectedRepository = repo;

        Assert.Equal(0, fireCount);
    }

    // --- Per-repo loading state ---

    [Fact]
    public void IsLoadingArchives_False_Initially()
    {
        var repo = new BorgRepository { Name = "r", Path = "/r" };
        _store.Add(repo);
        Assert.False(_store.IsLoadingArchives(repo));
    }

    [Fact]
    public void SetLoadingArchives_True_TracksPerRepo()
    {
        var repoA = new BorgRepository { Name = "a", Path = "/a" };
        var repoB = new BorgRepository { Name = "b", Path = "/b" };
        _store.Add(repoA);
        _store.Add(repoB);

        _store.SetLoadingArchives(repoA, true);

        Assert.True(_store.IsLoadingArchives(repoA));
        Assert.False(_store.IsLoadingArchives(repoB));
    }

    [Fact]
    public void SetLoadingArchives_False_ClearsState()
    {
        var repo = new BorgRepository { Name = "r", Path = "/r" };
        _store.Add(repo);
        _store.SetLoadingArchives(repo, true);

        _store.SetLoadingArchives(repo, false);

        Assert.False(_store.IsLoadingArchives(repo));
    }

    [Fact]
    public void SetLoadingArchives_FiresEventWhenSelectedRepoChanges()
    {
        var repo = new BorgRepository { Name = "r", Path = "/r" };
        _store.Add(repo);
        _store.SelectedRepository = repo;

        var fireCount = 0;
        _store.SelectedLoadingStateChanged += () => fireCount++;

        _store.SetLoadingArchives(repo, true);
        Assert.Equal(1, fireCount);

        _store.SetLoadingArchives(repo, false);
        Assert.Equal(2, fireCount);
    }

    [Fact]
    public void SetLoadingArchives_DoesNotFireWhenDifferentRepoChanges()
    {
        var repoA = new BorgRepository { Name = "a", Path = "/a" };
        var repoB = new BorgRepository { Name = "b", Path = "/b" };
        _store.Add(repoA);
        _store.Add(repoB);
        _store.SelectedRepository = repoA;

        var fired = false;
        _store.SelectedLoadingStateChanged += () => fired = true;

        _store.SetLoadingArchives(repoB, true);

        Assert.False(fired);
    }

    [Fact]
    public void SetLoadingArchives_DoesNotFireWhenValueUnchanged()
    {
        var repo = new BorgRepository { Name = "r", Path = "/r" };
        _store.Add(repo);
        _store.SelectedRepository = repo;
        _store.SetLoadingArchives(repo, true);

        var fireCount = 0;
        _store.SelectedLoadingStateChanged += () => fireCount++;

        _store.SetLoadingArchives(repo, true); // already true

        Assert.Equal(0, fireCount);
    }

    [Fact]
    public void Remove_ClearsLoadingState()
    {
        var repo = new BorgRepository { Name = "r", Path = "/r" };
        _store.Add(repo);
        _store.SetLoadingArchives(repo, true);

        _store.Remove(repo);

        Assert.False(_store.IsLoadingArchives(repo));
    }

    // --- HasError rollup (migrated from RepositoryListViewModelTests) ---

    [Fact]
    public void HandleJobCompleted_FailedResult_SetsErrorFlag()
    {
        var repo = new BorgRepository { Name = "r", Path = "/repo" };
        _store.Add(repo);

        // Stderr text that BorgErrorClassifier does not recognize, so ErrorMessage
        // falls through to the raw stderr string — see BorgResult.ErrorMessage.
        var job = new BorgJob { RepoPath = "/repo" };
        _store.HandleJobCompleted(job, new BorgResult(1, "", "borg: failed for some unclassified reason\nmore details"));

        Assert.True(repo.HasError);
        Assert.Equal("borg: failed for some unclassified reason\nmore details", repo.LastError);
    }

    [Fact]
    public void HandleJobCompleted_SuccessResult_ClearsErrorFlag()
    {
        var repo = new BorgRepository
        {
            Name = "r",
            Path = "/repo",
            HasError = true,
            LastError = "Old error",
        };
        _store.Add(repo);

        var job = new BorgJob { RepoPath = "/repo" };
        _store.HandleJobCompleted(job, new BorgResult(0, "ok", ""));

        Assert.False(repo.HasError);
        Assert.Null(repo.LastError);
    }

    [Fact]
    public void HandleJobCompleted_CancelledResult_DoesNotTouchFlag()
    {
        var repo = new BorgRepository
        {
            Name = "r",
            Path = "/repo",
            HasError = true,
            LastError = "Previous failure",
        };
        _store.Add(repo);

        var job = new BorgJob { RepoPath = "/repo" };
        _store.HandleJobCompleted(job, new BorgResult(-1, "", "", WasCancelled: true));

        // Cancellation preserves whatever state was there before.
        Assert.True(repo.HasError);
        Assert.Equal("Previous failure", repo.LastError);
    }

    [Fact]
    public void HandleJobCompleted_QueryJobFailure_AlsoSetsErrorFlag()
    {
        // Per spec: flag is set by any failed operation regardless of Kind.
        var repo = new BorgRepository { Name = "r", Path = "/repo" };
        _store.Add(repo);

        var job = new BorgJob { RepoPath = "/repo", Kind = BorgJobKind.Query };
        _store.HandleJobCompleted(job, new BorgResult(2, "", "query-level borg failure"));

        Assert.True(repo.HasError);
        Assert.Equal("query-level borg failure", repo.LastError);
    }

    [Fact]
    public void HandleJobCompleted_QueryJobSuccess_ClearsErrorFlag()
    {
        var repo = new BorgRepository
        {
            Name = "r",
            Path = "/repo",
            HasError = true,
            LastError = "stale",
        };
        _store.Add(repo);

        var job = new BorgJob { RepoPath = "/repo", Kind = BorgJobKind.Query };
        _store.HandleJobCompleted(job, new BorgResult(0, "[]", ""));

        Assert.False(repo.HasError);
    }

    [Fact]
    public void HandleJobCompleted_UnknownRepoPath_NoOp()
    {
        var repo = new BorgRepository
        {
            Name = "r",
            Path = "/repo",
            HasError = true,
            LastError = "stale",
        };
        _store.Add(repo);

        var job = new BorgJob { RepoPath = "/other" };
        _store.HandleJobCompleted(job, new BorgResult(0, "", ""));

        // Untouched — the job is for a different repo.
        Assert.True(repo.HasError);
        Assert.Equal("stale", repo.LastError);
    }

    [Fact]
    public void HandleJobCompleted_NullRepoPath_NoOp()
    {
        var repo = new BorgRepository
        {
            Name = "r",
            Path = "/repo",
            HasError = true,
            LastError = "stale",
        };
        _store.Add(repo);

        var job = new BorgJob { RepoPath = null };
        _store.HandleJobCompleted(job, new BorgResult(1, "", "anything"));

        Assert.True(repo.HasError);
        Assert.Equal("stale", repo.LastError);
    }
}
