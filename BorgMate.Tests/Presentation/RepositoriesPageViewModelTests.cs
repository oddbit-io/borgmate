using BorgMate.Domain.Settings;
using BorgMate.Infrastructure.Borg;
using BorgMate.Localization;
using BorgMate.Persistence.Configuration;
using BorgMate.Presentation.Dialogs;
using BorgMate.Runtime.Jobs;
using BorgMate.Runtime.Operations;
using BorgMate.Runtime.Security;
using Microsoft.Extensions.Logging;
using NSubstitute;
using BorgArchive = BorgMate.Domain.Archives.BorgArchive;
using BorgJob = BorgMate.Runtime.Jobs.BorgJob;
using RepositoriesPageViewModel = BorgMate.Presentation.ViewModels.RepositoriesPageViewModel;
using RepositoryPresentationState = BorgMate.Presentation.State.RepositoryPresentationState;
using RepositoryViewModel = BorgMate.Presentation.ViewModels.RepositoryViewModel;

namespace BorgMate.Tests.Presentation;

public class RepositoriesPageViewModelTests : IDisposable
{
    private readonly ISettingsStore _settingsStore = Substitute.For<ISettingsStore>();
    private readonly IRepositoryConfigStore _repositoryConfigStore = Substitute.For<IRepositoryConfigStore>();
    private readonly IRepositoryOperationRunner _repositoryOperations = Substitute.For<IRepositoryOperationRunner>();
    private readonly IArchiveOperationRunner _archiveOperations = Substitute.For<IArchiveOperationRunner>();
    private readonly IRepositoryVerificationOperationRunner _verificationOperations = Substitute.For<IRepositoryVerificationOperationRunner>();
    private readonly IPassphraseProvider _passphraseProvider = Substitute.For<IPassphraseProvider>();
    private readonly BorgJobQueue _jobQueue = new();
    private readonly RepositoryPresentationState _store;

    public RepositoriesPageViewModelTests()
    {
        _store = new RepositoryPresentationState();
        _repositoryConfigStore.GenerateUniqueCopyName(Arg.Any<string>())
            .Returns(call => GenerateUniqueCopyNameFromUiStore(call.Arg<string>()));
    }

    private string GenerateUniqueCopyNameFromUiStore(string baseName)
    {
        var existing = new HashSet<string>(_store.Repositories.Select(r => r.Name), StringComparer.Ordinal);
        var first = string.Format(Strings.Get("Duplicate.NameCopy"), baseName);
        if (!existing.Contains(first)) return first;

        for (var i = 2; ; i++)
        {
            var candidate = string.Format(Strings.Get("Duplicate.NameCopyN"), baseName, i);
            if (!existing.Contains(candidate)) return candidate;
        }
    }

    private RepositoriesPageViewModel CreateVm()
    {
        var filePicker = new FilePickerService();
        var logger = Substitute.For<ILogger<RepositoriesPageViewModel>>();
        var settings = new AppSettings(
            new BorgPathDetectionResult("borg", null, null),
            new BorgPathDetectionResult("borg2", null, null));
        return new RepositoriesPageViewModel(
            settings,
            _settingsStore,
            _repositoryConfigStore,
            filePicker,
            _repositoryOperations,
            _archiveOperations,
            _verificationOperations,
            new BorgCapabilityQuery(settings),
            _passphraseProvider,
            _jobQueue,
            _store,
            logger);
    }

    public void Dispose()
    {
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
        var repo = new RepositoryViewModel { Name = "test", Path = "/test" };
        vm.Repositories.Add(repo);
        vm.SelectedRepository = repo;

        Assert.True(vm.CanEditOrRemove);
    }

    [Fact]
    public void CanEditOrRemove_False_WhenBusy()
    {
        var vm = CreateVm();
        var repo = new RepositoryViewModel { Name = "test", Path = "/test" };
        vm.Repositories.Add(repo);
        vm.SelectedRepository = repo;

        repo.IsBusy = true;

        Assert.False(vm.CanEditOrRemove);
    }

    [Fact]
    public void CanRunBackup_False_WhenNoSourceDirs()
    {
        var vm = CreateVm();
        var repo = new RepositoryViewModel { Name = "test", Path = "/test" };
        vm.Repositories.Add(repo);
        vm.SelectedRepository = repo;

        Assert.False(vm.CanRunBackup);
    }

    [Fact]
    public void CanRunBackup_True_WhenHasSourceDirs()
    {
        var vm = CreateVm();
        var repo = new RepositoryViewModel { Name = "test", Path = "/test" };
        repo.SourceDirectories.Add("/home/user/docs");
        vm.Repositories.Add(repo);
        vm.SelectedRepository = repo;

        Assert.True(vm.CanRunBackup);
    }

    [Fact]
    public void CanRunBackup_False_WhenBusy()
    {
        var vm = CreateVm();
        var repo = new RepositoryViewModel { Name = "test", Path = "/test" };
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
        var repo1 = new RepositoryViewModel { Name = "repo1", Path = "/r1" };
        var repo2 = new RepositoryViewModel { Name = "repo2", Path = "/r2" };
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
        var repo = new RepositoryViewModel { Name = "test", Path = "/test" };
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
        var repo = new RepositoryViewModel { Name = "test", Path = "/test" };
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
        var repo = new RepositoryViewModel { Name = "r", Path = "/r" };
        Assert.False(repo.HasStats);
        Assert.Null(repo.StatsTotalSize);
    }

    [Fact]
    public void HandleJobCompleted_FailedResult_SetsErrorFlag()
    {
        var vm = CreateVm();
        var repo = new RepositoryViewModel { Name = "r", Path = "/repo" };
        vm.Repositories.Add(repo);

        var job = new BorgJob { RepoPath = "/repo" };
        vm.HandleJobCompleted(new JobCompleted(
            job,
            new BorgResult(150, "", "borg: failed for some unclassified reason\nmore details")));

        Assert.True(repo.HasError);
        Assert.Equal(
            $"Borg failed with unknown exit code 150.{Environment.NewLine}{Environment.NewLine}borg: failed for some unclassified reason{Environment.NewLine}more details",
            repo.LastError);
    }

    [Fact]
    public void HandleJobCompleted_SuccessResult_ClearsErrorFlag()
    {
        var vm = CreateVm();
        var repo = new RepositoryViewModel
        {
            Name = "r",
            Path = "/repo",
            HasError = true,
            LastError = "Old error",
        };
        vm.Repositories.Add(repo);

        var job = new BorgJob { RepoPath = "/repo" };
        vm.HandleJobCompleted(new JobCompleted(job, new BorgResult(0, "ok", "")));

        Assert.False(repo.HasError);
        Assert.Null(repo.LastError);
    }

    [Fact]
    public void HandleJobCompleted_CancelledResult_DoesNotTouchFlag()
    {
        var vm = CreateVm();
        var repo = new RepositoryViewModel
        {
            Name = "r",
            Path = "/repo",
            HasError = true,
            LastError = "Previous failure",
        };
        vm.Repositories.Add(repo);

        var job = new BorgJob { RepoPath = "/repo" };
        vm.HandleJobCompleted(new JobCompleted(job, new BorgResult(-1, "", "", WasCancelled: true)));

        Assert.True(repo.HasError);
        Assert.Equal("Previous failure", repo.LastError);
    }

    [Fact]
    public void HandleJobCompleted_QueryJobFailure_AlsoSetsErrorFlag()
    {
        var vm = CreateVm();
        var repo = new RepositoryViewModel { Name = "r", Path = "/repo" };
        vm.Repositories.Add(repo);

        var job = new BorgJob { RepoPath = "/repo", Kind = BorgJobKind.Query };
        vm.HandleJobCompleted(new JobCompleted(job, new BorgResult(2, "", "query-level borg failure")));

        Assert.True(repo.HasError);
        Assert.Equal(
            $"Borg failed with a generic error.{Environment.NewLine}{Environment.NewLine}query-level borg failure",
            repo.LastError);
    }

    [Fact]
    public void HandleJobCompleted_QueryJobFailureWhileBusy_KeepsRepositoryHealthy()
    {
        var vm = CreateVm();
        var repo = new RepositoryViewModel { Name = "r", Path = "/repo", IsBusy = true };
        vm.Repositories.Add(repo);

        var job = new BorgJob { RepoPath = "/repo", Kind = BorgJobKind.Query };
        vm.HandleJobCompleted(new JobCompleted(job, new BorgResult(73, "", "lock timed out")));

        Assert.False(repo.HasError);
        Assert.Null(repo.LastError);
    }

    [Fact]
    public void RepositoryBecomingBusy_ClearsPreviousFailure()
    {
        var repo = new RepositoryViewModel
        {
            Name = "r",
            Path = "/repo",
            HasError = true,
            LastError = "Borg repository lock timed out.",
        };

        repo.IsBusy = true;

        Assert.False(repo.HasError);
        Assert.Null(repo.LastError);
    }

    [Fact]
    public void HandleJobCompleted_UnknownRepoPath_NoOp()
    {
        var vm = CreateVm();
        var repo = new RepositoryViewModel
        {
            Name = "r",
            Path = "/repo",
            HasError = true,
            LastError = "stale",
        };
        vm.Repositories.Add(repo);

        var job = new BorgJob { RepoPath = "/other" };
        vm.HandleJobCompleted(new JobCompleted(job, new BorgResult(0, "", "")));

        Assert.True(repo.HasError);
        Assert.Equal("stale", repo.LastError);
    }

    [Fact]
    public void SelectedRepository_Changing_UnsubscribesOldRepo()
    {
        var vm = CreateVm();
        var repo1 = new RepositoryViewModel { Name = "r1", Path = "/r1" };
        repo1.SourceDirectories.Add("/data");
        var repo2 = new RepositoryViewModel { Name = "r2", Path = "/r2" };
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

        var newRepo = new RepositoryViewModel { Name = "new", Path = "/new" };
        vm.Repositories.Add(newRepo);
        vm.SelectedRepository = newRepo;

        Assert.Empty(vm.Archives);
        Assert.Null(vm.SelectedArchive);
    }

    [Fact]
    public void SelectedRepository_Changed_CachesOldArchives()
    {
        var vm = CreateVm();
        var repo1 = new RepositoryViewModel { Name = "repo1", Path = "/repo1" };
        var repo2 = new RepositoryViewModel { Name = "repo2", Path = "/repo2" };
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
        var repo = new RepositoryViewModel { Name = "test", Path = "/test" };
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
        var repo = new RepositoryViewModel { Name = "test", Path = "/test" };
        vm.Repositories.Add(repo);
        vm.SelectedRepository = repo;
        Assert.False(vm.PickAndRestoreCommand.CanExecute(null));
    }

    [Fact]
    public void PickAndRestore_RepoBusy_CannotExecute()
    {
        var vm = CreateVm();
        var repo = new RepositoryViewModel { Name = "test", Path = "/test", IsBusy = true };
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
        var repo = new RepositoryViewModel { Name = "test", Path = "/test" };
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
        var repo = new RepositoryViewModel { Name = "test", Path = "/test" };
        vm.Repositories.Add(repo);
        vm.SelectedRepository = repo;
        Assert.False(vm.DeleteArchiveCommand.CanExecute(null));
    }

    [Fact]
    public void BrowseAndRestore_NoArchive_CannotExecute()
    {
        var vm = CreateVm();
        var repo = new RepositoryViewModel { Name = "test", Path = "/test" };
        vm.Repositories.Add(repo);
        vm.SelectedRepository = repo;
        Assert.False(vm.BrowseAndRestoreCommand.CanExecute(null));
    }

    [Fact]
    public void CanExecute_RefreshedWhenRepoIsBusyToggles()
    {
        var vm = CreateVm();
        var repo = new RepositoryViewModel { Name = "test", Path = "/test" };
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
        var repo = new RepositoryViewModel { Name = "r", Path = "/r", HasError = true };
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
        var repo = new RepositoryViewModel { Name = "r", Path = "/r", HasError = true };
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
        var repo = new RepositoryViewModel { Name = "r", Path = "/r" };
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
        var repoA = new RepositoryViewModel { Name = "A", Path = "/A", HasError = true };
        var repoB = new RepositoryViewModel { Name = "B", Path = "/B" };
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

        var repo = new RepositoryViewModel { Name = "A", Path = "/A" };
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
        var repo = new RepositoryViewModel { Name = "r", Path = "/r", HasError = true };
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
    public void StoreInvalidation_SelectedRepository_ClearsVisibleArchives()
    {
        var vm = CreateVm();
        var repo = new RepositoryViewModel { Name = "r", Path = "/r" };
        repo.LoadedArchives = [new BorgArchive("a", DateTime.Now)];
        vm.Repositories.Add(repo);
        vm.SelectedRepository = repo;
        vm.SelectedArchive = vm.Archives.Single();

        _store.InvalidateArchives(repo);

        Assert.Empty(vm.Archives);
        Assert.Null(vm.SelectedArchive);
        _archiveOperations.Received(1).InvalidateRepositoryCache(repo);
    }

    [Fact]
    public void StoreInvalidation_BackgroundRepository_PreservesVisibleArchives()
    {
        var vm = CreateVm();
        var selected = new RepositoryViewModel { Name = "selected", Path = "/selected" };
        selected.LoadedArchives = [new BorgArchive("visible", DateTime.Now)];
        var background = new RepositoryViewModel { Name = "background", Path = "/background" };
        background.LoadedArchives = [new BorgArchive("cached", DateTime.Now)];
        vm.Repositories.Add(selected);
        vm.Repositories.Add(background);
        vm.SelectedRepository = selected;

        _store.InvalidateArchives(background);

        Assert.Equal("visible", Assert.Single(vm.Archives).Name);
        Assert.Null(background.LoadedArchives);
        _archiveOperations.Received(1).InvalidateRepositoryCache(background);
    }

    [Fact]
    public void FetchArchivesIfEmpty_RepoHasError_NoOp()
    {
        var vm = CreateVm();
        var repo = new RepositoryViewModel { Name = "r", Path = "/r", HasError = true };
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
        var repo = new RepositoryViewModel { Name = "r", Path = "/r", HasError = true };
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
        var repo = new RepositoryViewModel { Name = "r", Path = "/r", HasError = true };
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
        _store.Add(new RepositoryViewModel { Name = "Foo (Copy)", Path = "/a" });

        Assert.Equal("Foo (Copy 2)", vm.GenerateUniqueCopyName("Foo"));
    }

    [Fact]
    public void GenerateUniqueCopyName_MultipleCollisions_WalksToNextFree()
    {
        var vm = CreateVm();
        _store.Add(new RepositoryViewModel { Name = "Foo (Copy)", Path = "/a" });
        _store.Add(new RepositoryViewModel { Name = "Foo (Copy 2)", Path = "/b" });
        _store.Add(new RepositoryViewModel { Name = "Foo (Copy 3)", Path = "/c" });

        Assert.Equal("Foo (Copy 4)", vm.GenerateUniqueCopyName("Foo"));
    }
}
