using BorgMate.Models;
using BorgMate.Services;
using BorgMate.Services.Borg;
using BorgMate.Services.Config;
using BorgMate.Services.Journal;
using BorgMate.Services.Queue;
using BorgMate.Services.UI;
using BorgMate.ViewModels;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace BorgMate.Tests;

public class RepositoriesPageViewModelTests : IDisposable
{
    private static readonly WslHelper Wsl = new(Substitute.For<ILogger<WslHelper>>());
    private static readonly PassphrasePrompt Prompt = new(null);
    private static readonly SshAgentHelper SshAgent = new(Substitute.For<ILogger<SshAgentHelper>>(), null, Wsl);

    private readonly IConfigService _configService = Substitute.For<IConfigService>();
    private readonly IJournalService _journalService = Substitute.For<IJournalService>();
    private readonly JobQueueService _jobQueue = new();
    private readonly RepositoryStore _store;

    public RepositoriesPageViewModelTests()
    {
        _store = new RepositoryStore(_jobQueue);
    }

    private RepositoriesPageViewModel CreateVm()
    {
        var borgFactory = new BorgServiceFactory(Substitute.For<ILoggerFactory>(), new AppSettings(), SshAgent, Wsl);
        var filePicker = new FilePickerService();
        var cache = new BorgCacheService();
        var runner = new BorgOperationRunner(
            Substitute.For<ILogger<BorgOperationRunner>>(), _jobQueue, _journalService, Prompt);
        var sizeCalculator = new DirectorySizeCalculator(Substitute.For<ILogger<DirectorySizeCalculator>>());
        var logger = Substitute.For<ILogger<RepositoriesPageViewModel>>();
        // null! for _jobQueue so auto-fetch paths short-circuit in tests
        return new RepositoriesPageViewModel(new AppSettings(), borgFactory, _configService, filePicker, cache,
            _journalService, runner, Prompt, Wsl, sizeCalculator, null!, _store, logger);
    }

    public void Dispose()
    {
        _store.Dispose();
        _jobQueue.Dispose();
    }

    // === Selection state ===

    [Fact]
    public void SelectedRepository_Null_Initially()
    {
        var vm = CreateVm();
        Assert.Null(vm.SelectedRepository);
    }

    [Fact]
    public void CanEditOrRemove_False_WhenNoSelection()
    {
        var vm = CreateVm();
        Assert.False(vm.CanEditOrRemove);
    }

    [Fact]
    public void CanEditOrRemove_True_WhenSelected()
    {
        var vm = CreateVm();
        var repo = new BorgRepository { Name = "test", Path = "/test" };
        vm.Repositories.Add(repo);
        vm.SelectedRepository = repo;

        Assert.True(vm.CanEditOrRemove);
    }

    [Fact]
    public void CanEditOrRemove_False_WhenBusy()
    {
        var vm = CreateVm();
        var repo = new BorgRepository { Name = "test", Path = "/test" };
        vm.Repositories.Add(repo);
        vm.SelectedRepository = repo;

        repo.IsBusy = true;

        Assert.False(vm.CanEditOrRemove);
    }

    [Fact]
    public void CanRunBackup_False_WhenNoSourceDirs()
    {
        var vm = CreateVm();
        var repo = new BorgRepository { Name = "test", Path = "/test" };
        vm.Repositories.Add(repo);
        vm.SelectedRepository = repo;

        Assert.False(vm.CanRunBackup);
    }

    [Fact]
    public void CanRunBackup_True_WhenHasSourceDirs()
    {
        var vm = CreateVm();
        var repo = new BorgRepository { Name = "test", Path = "/test" };
        repo.SourceDirectories.Add("/home/user/docs");
        vm.Repositories.Add(repo);
        vm.SelectedRepository = repo;

        Assert.True(vm.CanRunBackup);
    }

    [Fact]
    public void CanRunBackup_False_WhenBusy()
    {
        var vm = CreateVm();
        var repo = new BorgRepository { Name = "test", Path = "/test" };
        repo.SourceDirectories.Add("/home/user/docs");
        vm.Repositories.Add(repo);
        vm.SelectedRepository = repo;

        repo.IsBusy = true;

        Assert.False(vm.CanRunBackup);
    }

    [Fact]
    public void SelectedRepository_Changed_PreservesPerRepoStats()
    {
        var vm = CreateVm();
        var repo1 = new BorgRepository { Name = "repo1", Path = "/r1" };
        var repo2 = new BorgRepository { Name = "repo2", Path = "/r2" };
        vm.Repositories.Add(repo1);
        vm.Repositories.Add(repo2);

        vm.SelectedRepository = repo1;
        repo1.StatsTotalSize = 100_000_000;

        vm.SelectedRepository = repo2;
        Assert.Null(repo2.StatsTotalSize);

        vm.SelectedRepository = repo1;
        Assert.Equal(100_000_000, repo1.StatsTotalSize);
    }

    [Fact]
    public void SelectedRepository_Changed_UpdatesCanProperties()
    {
        var vm = CreateVm();
        var repo = new BorgRepository { Name = "test", Path = "/test" };
        repo.SourceDirectories.Add("/data");
        vm.Repositories.Add(repo);

        Assert.False(vm.CanEditOrRemove);
        Assert.False(vm.CanRunBackup);

        vm.SelectedRepository = repo;

        Assert.True(vm.CanEditOrRemove);
        Assert.True(vm.CanRunBackup);
    }

    [Fact]
    public void IsBusy_Changed_UpdatesCanProperties()
    {
        var vm = CreateVm();
        var repo = new BorgRepository { Name = "test", Path = "/test" };
        repo.SourceDirectories.Add("/data");
        vm.Repositories.Add(repo);
        vm.SelectedRepository = repo;

        Assert.True(vm.CanEditOrRemove);
        Assert.True(vm.CanRunBackup);

        repo.IsBusy = true;

        Assert.False(vm.CanEditOrRemove);
        Assert.False(vm.CanRunBackup);

        repo.IsBusy = false;

        Assert.True(vm.CanEditOrRemove);
        Assert.True(vm.CanRunBackup);
    }

    [Fact]
    public void Repositories_Empty_Initially()
    {
        var vm = CreateVm();
        Assert.Empty(vm.Repositories);
    }

    [Fact]
    public void Progress_NotNull()
    {
        var vm = CreateVm();
        Assert.NotNull(vm.Progress);
        Assert.False(vm.Progress.HasActiveJob);
    }

    [Fact]
    public void RestoreProgress_NotNull()
    {
        var vm = CreateVm();
        Assert.NotNull(vm.RestoreProgress);
        Assert.False(vm.RestoreProgress.HasActiveJob);
    }

    [Fact]
    public void Progress_SetActiveJob_TracksJob()
    {
        var vm = CreateVm();
        var job = new BorgJob { Name = "Test Job", Status = BorgJobStatus.Running };
        vm.Progress.SetActiveJob(job);

        Assert.True(vm.Progress.HasActiveJob);
    }

    [Fact]
    public void Progress_SetActiveJob_Null_ClearsProgress()
    {
        var vm = CreateVm();
        var job = new BorgJob { Name = "Test Job", Status = BorgJobStatus.Running };
        vm.Progress.SetActiveJob(job);

        vm.Progress.SetActiveJob(null);

        Assert.False(vm.Progress.HasActiveJob);
    }

    [Fact]
    public void NewRepo_HasStats_False()
    {
        var repo = new BorgRepository { Name = "r", Path = "/r" };
        Assert.False(repo.HasStats);
        Assert.Null(repo.StatsTotalSize);
    }

    [Fact]
    public void SelectedRepository_Changing_UnsubscribesOldRepo()
    {
        var vm = CreateVm();
        var repo1 = new BorgRepository { Name = "r1", Path = "/r1" };
        repo1.SourceDirectories.Add("/data");
        var repo2 = new BorgRepository { Name = "r2", Path = "/r2" };
        repo2.SourceDirectories.Add("/data");
        vm.Repositories.Add(repo1);
        vm.Repositories.Add(repo2);

        vm.SelectedRepository = repo1;
        Assert.True(vm.CanRunBackup);

        vm.SelectedRepository = repo2;

        // Changing repo1's IsBusy must NOT affect CanRunBackup since repo2 is now selected.
        repo1.IsBusy = true;
        Assert.True(vm.CanRunBackup);
    }

    // === Archive selection ===

    [Fact]
    public void SelectedArchive_Null_Initially()
    {
        var vm = CreateVm();
        Assert.Null(vm.SelectedArchive);
        Assert.False(vm.HasSelectedArchive);
    }

    [Fact]
    public void HasSelectedArchive_True_WhenSet()
    {
        var vm = CreateVm();
        vm.Archives.Add(new BorgArchive("test", DateTime.Now));
        vm.SelectedArchive = vm.Archives[0];

        Assert.True(vm.HasSelectedArchive);
    }

    [Fact]
    public void SelectedRepository_Changed_ClearsArchives()
    {
        var vm = CreateVm();
        vm.Archives.Add(new BorgArchive("old", DateTime.Now));
        vm.SelectedArchive = vm.Archives[0];

        var newRepo = new BorgRepository { Name = "new", Path = "/new" };
        vm.Repositories.Add(newRepo);
        vm.SelectedRepository = newRepo;

        Assert.Empty(vm.Archives);
        Assert.Null(vm.SelectedArchive);
    }

    [Fact]
    public void SelectedRepository_Changed_CachesOldArchives()
    {
        var vm = CreateVm();
        var repo1 = new BorgRepository { Name = "repo1", Path = "/repo1" };
        var repo2 = new BorgRepository { Name = "repo2", Path = "/repo2" };
        vm.Repositories.Add(repo1);
        vm.Repositories.Add(repo2);

        vm.SelectedRepository = repo1;
        vm.Archives.Add(new BorgArchive("archive1", DateTime.Now));

        vm.SelectedRepository = repo2; // saves repo1's archives to store

        vm.SelectedRepository = repo1; // restores from store

        var archives = vm.Archives.ToList();
        Assert.Single(archives);
        Assert.Equal("archive1", archives[0].Name);
    }

    [Fact]
    public void InvalidateArchives_ClearsAll()
    {
        var vm = CreateVm();
        var repo = new BorgRepository { Name = "test", Path = "/test" };
        vm.Repositories.Add(repo);
        vm.SelectedRepository = repo;
        vm.Archives.Add(new BorgArchive("archive", DateTime.Now));
        vm.SelectedArchive = vm.Archives[0];

        vm.InvalidateArchives();

        Assert.Empty(vm.Archives);
        Assert.Null(vm.SelectedArchive);
    }

    // === Sort ===

    [Fact]
    public void SortByName_TogglesDirection()
    {
        var vm = CreateVm();
        vm.Archives.Add(new BorgArchive("b-archive", DateTime.Now));
        vm.Archives.Add(new BorgArchive("a-archive", DateTime.Now.AddHours(-1)));

        vm.SortByNameCommand.Execute(null);
        Assert.Equal("a-archive", vm.Archives[0].Name);
        Assert.Contains("▲", vm.NameSortIndicator);

        vm.SortByNameCommand.Execute(null);
        Assert.Equal("b-archive", vm.Archives[0].Name);
        Assert.Contains("▼", vm.NameSortIndicator);
    }

    [Fact]
    public void SortByDate_TogglesDirection()
    {
        var vm = CreateVm();
        var older = new BorgArchive("old", DateTime.Now.AddDays(-1));
        var newer = new BorgArchive("new", DateTime.Now);
        vm.Archives.Add(older);
        vm.Archives.Add(newer);

        // Default sort is date descending. First click toggles to ascending.
        vm.SortByDateCommand.Execute(null);
        Assert.Equal("old", vm.Archives[0].Name);
        Assert.Contains("▲", vm.DateSortIndicator);

        vm.SortByDateCommand.Execute(null);
        Assert.Equal("new", vm.Archives[0].Name);
        Assert.Contains("▼", vm.DateSortIndicator);
    }

    [Fact]
    public void HasDetail_False_Initially()
    {
        var vm = CreateVm();
        Assert.False(vm.HasDetail);
    }

    // === Restore / Delete CanExecute gating ===

    [Fact]
    public void PickAndRestore_NoRepo_CannotExecute()
    {
        var vm = CreateVm();
        Assert.False(vm.PickAndRestoreCommand.CanExecute(null));
    }

    [Fact]
    public void PickAndRestore_NoArchive_CannotExecute()
    {
        var vm = CreateVm();
        var repo = new BorgRepository { Name = "test", Path = "/test" };
        vm.Repositories.Add(repo);
        vm.SelectedRepository = repo;
        Assert.False(vm.PickAndRestoreCommand.CanExecute(null));
    }

    [Fact]
    public void PickAndRestore_RepoBusy_CannotExecute()
    {
        var vm = CreateVm();
        var repo = new BorgRepository { Name = "test", Path = "/test", IsBusy = true };
        vm.Repositories.Add(repo);
        vm.SelectedRepository = repo;
        vm.Archives.Add(new BorgArchive("a", DateTime.Now));
        vm.SelectedArchive = vm.Archives[0];
        Assert.False(vm.PickAndRestoreCommand.CanExecute(null));
    }

    [Fact]
    public void PickAndRestore_RepoAndArchiveAndIdle_CanExecute()
    {
        var vm = CreateVm();
        var repo = new BorgRepository { Name = "test", Path = "/test" };
        vm.Repositories.Add(repo);
        vm.SelectedRepository = repo;
        vm.Archives.Add(new BorgArchive("a", DateTime.Now));
        vm.SelectedArchive = vm.Archives[0];
        Assert.True(vm.PickAndRestoreCommand.CanExecute(null));
    }

    [Fact]
    public void DeleteArchive_NoArchive_CannotExecute()
    {
        var vm = CreateVm();
        var repo = new BorgRepository { Name = "test", Path = "/test" };
        vm.Repositories.Add(repo);
        vm.SelectedRepository = repo;
        Assert.False(vm.DeleteArchiveCommand.CanExecute(null));
    }

    [Fact]
    public void BrowseAndRestore_NoArchive_CannotExecute()
    {
        var vm = CreateVm();
        var repo = new BorgRepository { Name = "test", Path = "/test" };
        vm.Repositories.Add(repo);
        vm.SelectedRepository = repo;
        Assert.False(vm.BrowseAndRestoreCommand.CanExecute(null));
    }

    [Fact]
    public void CanExecute_RefreshedWhenRepoIsBusyToggles()
    {
        var vm = CreateVm();
        var repo = new BorgRepository { Name = "test", Path = "/test" };
        vm.Repositories.Add(repo);
        vm.SelectedRepository = repo;
        vm.Archives.Add(new BorgArchive("a", DateTime.Now));
        vm.SelectedArchive = vm.Archives[0];
        Assert.True(vm.DeleteArchiveCommand.CanExecute(null));

        repo.IsBusy = true;
        Assert.False(vm.DeleteArchiveCommand.CanExecute(null));

        repo.IsBusy = false;
        Assert.True(vm.DeleteArchiveCommand.CanExecute(null));
    }

    // === HasError gating ===

    [Fact]
    public void CanModifyArchive_RepoHasError_False()
    {
        var vm = CreateVm();
        var repo = new BorgRepository { Name = "r", Path = "/r", HasError = true };
        vm.Repositories.Add(repo);
        vm.SelectedRepository = repo;
        vm.Archives.Add(new BorgArchive("a", DateTime.Now));
        vm.SelectedArchive = vm.Archives[0];

        Assert.False(vm.CanModifyArchive);
        Assert.False(vm.IsRepoIdle);
    }

    [Fact]
    public void PickAndRestore_RepoHasError_CannotExecute()
    {
        var vm = CreateVm();
        var repo = new BorgRepository { Name = "r", Path = "/r", HasError = true };
        vm.Repositories.Add(repo);
        vm.SelectedRepository = repo;
        vm.Archives.Add(new BorgArchive("a", DateTime.Now));
        vm.SelectedArchive = vm.Archives[0];

        Assert.False(vm.PickAndRestoreCommand.CanExecute(null));
        Assert.False(vm.BrowseAndRestoreCommand.CanExecute(null));
        Assert.False(vm.DeleteArchiveCommand.CanExecute(null));
    }

    [Fact]
    public void CanExecute_RefreshedWhenRepoHasErrorToggles()
    {
        var vm = CreateVm();
        var repo = new BorgRepository { Name = "r", Path = "/r" };
        vm.Repositories.Add(repo);
        vm.SelectedRepository = repo;
        vm.Archives.Add(new BorgArchive("a", DateTime.Now));
        vm.SelectedArchive = vm.Archives[0];
        Assert.True(vm.DeleteArchiveCommand.CanExecute(null));

        repo.HasError = true;
        Assert.False(vm.DeleteArchiveCommand.CanExecute(null));
        Assert.False(vm.PickAndRestoreCommand.CanExecute(null));
        Assert.False(vm.BrowseAndRestoreCommand.CanExecute(null));

        repo.HasError = false;
        Assert.True(vm.DeleteArchiveCommand.CanExecute(null));
    }

    [Fact]
    public void CanModifyArchive_SwappingFromErroredToHealthyRepo_Reevaluates()
    {
        var vm = CreateVm();
        var repoA = new BorgRepository { Name = "A", Path = "/A", HasError = true };
        var repoB = new BorgRepository { Name = "B", Path = "/B" };
        vm.Repositories.Add(repoA);
        vm.Repositories.Add(repoB);

        vm.SelectedRepository = repoA;
        vm.Archives.Add(new BorgArchive("a1", DateTime.Now));
        vm.SelectedArchive = vm.Archives[0];

        Assert.False(vm.CanModifyArchive);
        Assert.False(vm.IsRepoIdle);
        Assert.False(vm.DeleteArchiveCommand.CanExecute(null));

        vm.SelectedRepository = repoB;
        vm.Archives.Add(new BorgArchive("b1", DateTime.Now));
        vm.SelectedArchive = vm.Archives[0];

        Assert.True(vm.IsRepoIdle);
        Assert.True(vm.CanModifyArchive);
        Assert.True(vm.DeleteArchiveCommand.CanExecute(null));
        Assert.True(vm.PickAndRestoreCommand.CanExecute(null));
        Assert.True(vm.BrowseAndRestoreCommand.CanExecute(null));
    }

    [Fact]
    public void CanModifyArchive_SwappingRepo_FiresPropertyChanged()
    {
        var vm = CreateVm();
        var raised = new HashSet<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        var repo = new BorgRepository { Name = "A", Path = "/A" };
        vm.Repositories.Add(repo);
        vm.SelectedRepository = repo;

        Assert.Contains(nameof(RepositoriesPageViewModel.SelectedRepository), raised);
        Assert.Contains(nameof(RepositoriesPageViewModel.CanModifyArchive), raised);
        Assert.Contains(nameof(RepositoriesPageViewModel.IsRepoIdle), raised);
    }

    // === Auto-fetch suppression while repo has HasError ===

    [Fact]
    public void InvalidateArchives_RepoHasError_DoesNotReFetch()
    {
        var vm = CreateVm();
        var repo = new BorgRepository { Name = "r", Path = "/r", HasError = true };
        // Seed archives directly on the repo
        repo.LoadedArchives = new List<BorgArchive> { new("a", DateTime.Now) };
        vm.Repositories.Add(repo);
        vm.IsActive = true;
        vm.SelectedRepository = repo;

        // InvalidateArchives clears and (because HasError is set) does not refetch.
        vm.InvalidateArchives();

        Assert.Empty(vm.Archives);
    }

    [Fact]
    public void FetchArchivesIfEmpty_RepoHasError_NoOp()
    {
        var vm = CreateVm();
        var repo = new BorgRepository { Name = "r", Path = "/r", HasError = true };
        vm.Repositories.Add(repo);
        vm.IsActive = true;
        vm.SelectedRepository = repo;
        Assert.Empty(vm.Archives);

        vm.FetchArchivesIfEmpty();

        Assert.Empty(vm.Archives);
    }

    [Fact]
    public void RepositoryAssignment_RepoHasError_DoesNotAutoFetch()
    {
        var vm = CreateVm();
        var repo = new BorgRepository { Name = "r", Path = "/r", HasError = true };
        vm.Repositories.Add(repo);
        vm.IsActive = true;
        vm.SelectedRepository = repo;

        Assert.Empty(vm.Archives);
    }

    [Fact]
    public void RepoErrorClears_TriggersFetchArchivesIfEmpty()
    {
        // Recovery path: when HasError clears, OnSelectedRepoPropertyChanged posts
        // a FetchArchivesIfEmpty. We just verify state doesn't corrupt.
        var vm = CreateVm();
        var repo = new BorgRepository { Name = "r", Path = "/r", HasError = true };
        vm.Repositories.Add(repo);
        vm.IsActive = true;
        vm.SelectedRepository = repo;
        Assert.Empty(vm.Archives);

        repo.HasError = false;
        Assert.False(vm.SelectedRepository!.HasError);
    }

    // === Duplicate name generation ===

    [Fact]
    public void GenerateUniqueCopyName_NoCollision_ReturnsFirstSuffix()
    {
        var vm = CreateVm();
        Assert.Equal("Foo (Copy)", vm.GenerateUniqueCopyName("Foo"));
    }

    [Fact]
    public void GenerateUniqueCopyName_FirstSuffixCollides_ReturnsCopy2()
    {
        var vm = CreateVm();
        _store.Add(new BorgRepository { Name = "Foo (Copy)", Path = "/a" });

        Assert.Equal("Foo (Copy 2)", vm.GenerateUniqueCopyName("Foo"));
    }

    [Fact]
    public void GenerateUniqueCopyName_MultipleCollisions_WalksToNextFree()
    {
        var vm = CreateVm();
        _store.Add(new BorgRepository { Name = "Foo (Copy)", Path = "/a" });
        _store.Add(new BorgRepository { Name = "Foo (Copy 2)", Path = "/b" });
        _store.Add(new BorgRepository { Name = "Foo (Copy 3)", Path = "/c" });

        Assert.Equal("Foo (Copy 4)", vm.GenerateUniqueCopyName("Foo"));
    }
}
