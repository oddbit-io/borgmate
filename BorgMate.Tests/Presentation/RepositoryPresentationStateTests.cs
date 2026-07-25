using BorgMate.Domain.Archives;
using RepositoryPresentationState = BorgMate.Presentation.State.RepositoryPresentationState;
using RepositoryViewModel = BorgMate.Presentation.ViewModels.RepositoryViewModel;

namespace BorgMate.Tests.Presentation;

public class RepositoryPresentationStateTests
{
    private readonly RepositoryPresentationState _store;

    public RepositoryPresentationStateTests()
    {
        _store = new RepositoryPresentationState();
    }

    // --- Collection mutation ---

    [Fact]
    public void Add_AppendsToRepositories()
    {
        var repo = new RepositoryViewModel { Name = "r", Path = "/r" };
        _store.Add(repo);

        Assert.Single(_store.Repositories);
        Assert.Same(repo, _store.Repositories[0]);
    }

    [Fact]
    public void Remove_RemovesFromRepositories()
    {
        var repo = new RepositoryViewModel { Name = "r", Path = "/r" };
        _store.Add(repo);
        _store.Remove(repo);

        Assert.Empty(_store.Repositories);
    }

    [Fact]
    public void Remove_ClearsSelectionWhenRemovingSelectedRepo()
    {
        var repo = new RepositoryViewModel { Name = "r", Path = "/r" };
        _store.Add(repo);
        _store.SelectedRepository = repo;

        _store.Remove(repo);

        Assert.Null(_store.SelectedRepository);
    }

    [Fact]
    public void Remove_PreservesSelectionWhenRemovingDifferentRepo()
    {
        var repoA = new RepositoryViewModel { Name = "a", Path = "/a" };
        var repoB = new RepositoryViewModel { Name = "b", Path = "/b" };
        _store.Add(repoA);
        _store.Add(repoB);
        _store.SelectedRepository = repoA;

        _store.Remove(repoB);

        Assert.Same(repoA, _store.SelectedRepository);
    }

    [Fact]
    public void FindByPath_ReturnsMatchingRepo()
    {
        var repoA = new RepositoryViewModel { Name = "a", Path = "/a" };
        var repoB = new RepositoryViewModel { Name = "b", Path = "/b" };
        _store.Add(repoA);
        _store.Add(repoB);

        Assert.Same(repoB, _store.FindByPath("/b"));
    }

    [Fact]
    public void FindByPath_ReturnsNullWhenNotFound()
    {
        _store.Add(new RepositoryViewModel { Name = "a", Path = "/a" });

        Assert.Null(_store.FindByPath("/missing"));
    }

    [Fact]
    public void InvalidateArchives_ClearsOnlyTargetAndRaisesEvent()
    {
        var repoA = new RepositoryViewModel { Name = "a", Path = "/a" };
        var repoB = new RepositoryViewModel { Name = "b", Path = "/b" };
        repoA.LoadedArchives = [new BorgArchive("a1", DateTime.Now)];
        repoB.LoadedArchives = [new BorgArchive("b1", DateTime.Now)];
        _store.Add(repoA);
        _store.Add(repoB);

        RepositoryViewModel? invalidated = null;
        _store.ArchivesInvalidated += repo => invalidated = repo;

        _store.InvalidateArchives(repoA);

        Assert.Null(repoA.LoadedArchives);
        Assert.NotNull(repoB.LoadedArchives);
        Assert.Same(repoA, invalidated);
    }

    // --- SelectionChanged event ---

    [Fact]
    public void SelectionChanged_FiresWithOldAndNewValues()
    {
        var repoA = new RepositoryViewModel { Name = "a", Path = "/a" };
        var repoB = new RepositoryViewModel { Name = "b", Path = "/b" };
        _store.Add(repoA);
        _store.Add(repoB);

        RepositoryViewModel? capturedOld = null;
        RepositoryViewModel? capturedNew = null;
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
        var repo = new RepositoryViewModel { Name = "r", Path = "/r" };
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
        var repo = new RepositoryViewModel { Name = "r", Path = "/r" };
        _store.Add(repo);
        Assert.False(_store.IsLoadingArchives(repo));
    }

    [Fact]
    public void SetLoadingArchives_True_TracksPerRepo()
    {
        var repoA = new RepositoryViewModel { Name = "a", Path = "/a" };
        var repoB = new RepositoryViewModel { Name = "b", Path = "/b" };
        _store.Add(repoA);
        _store.Add(repoB);

        _store.SetLoadingArchives(repoA, true);

        Assert.True(_store.IsLoadingArchives(repoA));
        Assert.False(_store.IsLoadingArchives(repoB));
    }

    [Fact]
    public void SetLoadingArchives_False_ClearsState()
    {
        var repo = new RepositoryViewModel { Name = "r", Path = "/r" };
        _store.Add(repo);
        _store.SetLoadingArchives(repo, true);

        _store.SetLoadingArchives(repo, false);

        Assert.False(_store.IsLoadingArchives(repo));
    }

    [Fact]
    public void SetLoadingArchives_FiresEventWhenSelectedRepoChanges()
    {
        var repo = new RepositoryViewModel { Name = "r", Path = "/r" };
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
        var repoA = new RepositoryViewModel { Name = "a", Path = "/a" };
        var repoB = new RepositoryViewModel { Name = "b", Path = "/b" };
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
        var repo = new RepositoryViewModel { Name = "r", Path = "/r" };
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
        var repo = new RepositoryViewModel { Name = "r", Path = "/r" };
        _store.Add(repo);
        _store.SetLoadingArchives(repo, true);

        _store.Remove(repo);

        Assert.False(_store.IsLoadingArchives(repo));
    }

}
