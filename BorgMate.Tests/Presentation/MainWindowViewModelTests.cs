using BorgMate.Domain.Repositories;
using BorgMate.Domain.Settings;
using BorgMate.Infrastructure.Borg;
using BorgMate.Infrastructure.Platform;
using BorgMate.Persistence.Configuration;
using BorgMate.Persistence.Journal;
using BorgMate.Presentation.Dialogs;
using BorgMate.Runtime.Jobs;
using BorgMate.Runtime.Operations;
using BorgMate.Runtime.Scheduling;
using BorgMate.Runtime.Security;
using Microsoft.Extensions.Logging;
using NSubstitute;
using OneOf;
using BorgJob = BorgMate.Runtime.Jobs.BorgJob;
using JournalEntry = BorgMate.Domain.Journal.JournalEntry;
using MainWindowViewModel = BorgMate.Presentation.ViewModels.MainWindowViewModel;
using NotificationsViewModel = BorgMate.Presentation.ViewModels.NotificationsViewModel;
using RepositoriesPageViewModel = BorgMate.Presentation.ViewModels.RepositoriesPageViewModel;
using RepositoryPresentationState = BorgMate.Presentation.State.RepositoryPresentationState;
using UpdateStatusState = BorgMate.Presentation.State.UpdateStatusState;

namespace BorgMate.Tests.Presentation;

public class MainWindowViewModelTests : IDisposable
{
    private readonly ISettingsStore _settingsStore = Substitute.For<ISettingsStore>();
    private readonly IRepositoryConfigStore _repositoryConfigStore = Substitute.For<IRepositoryConfigStore>();
    private readonly AppSettings _settings = new(
        new BorgPathDetectionResult("borg", null, null),
        new BorgPathDetectionResult("borg2", null, null));
    private readonly IJournalStore _journalService;
    private readonly BorgJobQueue _jobQueue = new();
    private readonly RepositoryPresentationState _store;
    private readonly IBackupScheduler _scheduler = Substitute.For<IBackupScheduler>();
    private readonly IAutoStartService _autoStart = Substitute.For<IAutoStartService>();
    private readonly IWakeScheduler _wakeScheduler = Substitute.For<IWakeScheduler>();

    public MainWindowViewModelTests()
    {
        _journalService = Substitute.For<IJournalStore>();
        _journalService.Entries.Returns(new System.Collections.ObjectModel.ObservableCollection<JournalEntry>());
        _repositoryConfigStore.GetSnapshot().Returns(new RepositoryConfigSnapshot([]));
        _settingsStore.SaveAsync().Returns(Task.FromResult<OneOf<StoreOk, StoreError>>(StoreOk.Instance));
        _repositoryConfigStore.UpdateAsync(Arg.Any<BorgRepository>())
            .Returns(Task.FromResult<OneOf<RepositoryConfigSnapshot, StoreError>>(new RepositoryConfigSnapshot([])));
        _store = new RepositoryPresentationState();
    }

    private RepositoriesPageViewModel CreatePageVm()
    {
        var verificationOperations = Substitute.For<IRepositoryVerificationOperationRunner>();
        return new RepositoriesPageViewModel(
            _settings,
            _settingsStore,
            _repositoryConfigStore,
            new FilePickerService(),
            Substitute.For<IRepositoryOperationRunner>(),
            Substitute.For<IArchiveOperationRunner>(),
            verificationOperations,
            new BorgCapabilityQuery(_settings),
            Substitute.For<IPassphraseProvider>(),
            _jobQueue,
            _store,
            Substitute.For<ILogger<RepositoriesPageViewModel>>());
    }

    public void Dispose()
    {
        _jobQueue.Dispose();
    }

    // --- Parameterless Constructor ---

    [Fact]
    public void DefaultConstructor_DoesNotThrow()
    {
        var vm = new MainWindowViewModel();
        Assert.NotNull(vm);
    }

    // --- Navigation ---

    [Fact]
    public void ActivePage_DefaultsToRepositories()
    {
        var vm = new MainWindowViewModel();
        Assert.Equal(AppPage.Repositories, vm.ActivePage);
    }

    [Fact]
    public void IsRepositoriesPage_True_WhenActive()
    {
        var vm = new MainWindowViewModel();
        Assert.True(vm.IsRepositoriesPage);
        Assert.False(vm.IsNotificationsPage);
    }

    [Fact]
    public void NavigateTo_ChangesActivePage()
    {
        var page = CreatePageVm();
        var notifications = new NotificationsViewModel(_journalService);
        var vm = new MainWindowViewModel(_settingsStore, _repositoryConfigStore, _settings, page,
            notifications, _journalService, new UpdateStatusState(), _jobQueue, _store, _autoStart, _wakeScheduler,
            _scheduler);

        vm.NavigateToCommand.Execute(AppPage.Notifications);

        Assert.Equal(AppPage.Notifications, vm.ActivePage);
        Assert.True(vm.IsNotificationsPage);
        Assert.False(vm.IsRepositoriesPage);
    }

    // --- Sidebar ---

    [Fact]
    public void IsSidebarExpanded_False_Initially()
    {
        var vm = new MainWindowViewModel();
        Assert.False(vm.IsSidebarExpanded);
    }

    [Fact]
    public void ToggleSidebar_TogglesSidebarExpanded()
    {
        var vm = new MainWindowViewModel();

        vm.ToggleSidebarCommand.Execute(null);
        Assert.True(vm.IsSidebarExpanded);

        vm.ToggleSidebarCommand.Execute(null);
        Assert.False(vm.IsSidebarExpanded);
    }

    [Fact]
    public void SidebarWidth_ReflectsExpansionState()
    {
        var vm = new MainWindowViewModel();
        Assert.Equal(48, vm.SidebarWidth);

        vm.IsSidebarExpanded = true;
        Assert.Equal(200, vm.SidebarWidth);
    }

    // --- SelectedJob ---

    [Fact]
    public void HasSelectedJob_False_Initially()
    {
        var vm = new MainWindowViewModel();
        Assert.False(vm.HasSelectedJob);
    }

    [Fact]
    public void HasSelectedJob_True_WhenSet()
    {
        var vm = new MainWindowViewModel();
        vm.SelectedJob = new BorgJob { Name = "test" };
        Assert.True(vm.HasSelectedJob);
    }

    // --- SaveConfigAsync ---

    [Fact]
    public async Task SaveConfigAsync_PersistsSidebarState()
    {
        var page = CreatePageVm();
        var notifications = new NotificationsViewModel(_journalService);

        var vm = new MainWindowViewModel(_settingsStore, _repositoryConfigStore, _settings, page,
            notifications, _journalService, new UpdateStatusState(), _jobQueue, _store, _autoStart, _wakeScheduler,
            _scheduler);

        vm.IsSidebarExpanded = true;
        await vm.SaveConfigAsync();

        Assert.True(_settings.SidebarExpanded);
        await _settingsStore.Received().SaveAsync();
    }

    [Fact]
    public void Constructor_LoadsRepositoriesFromConfig()
    {
        _repositoryConfigStore.GetSnapshot().Returns(new RepositoryConfigSnapshot([
            new BorgRepository { Name = "repo1", Path = "/repo1" },
            new BorgRepository { Name = "repo2", Path = "/repo2" }
        ]));
        var page = CreatePageVm();
        var notifications = new NotificationsViewModel(_journalService);

        var vm = new MainWindowViewModel(_settingsStore, _repositoryConfigStore, _settings, page,
            notifications, _journalService, new UpdateStatusState(), _jobQueue, _store, _autoStart, _wakeScheduler,
            _scheduler);

        Assert.Equal(2, _store.Repositories.Count);
        Assert.Equal("repo1", _store.Repositories[0].Name);
        Assert.Equal("repo2", _store.Repositories[1].Name);
    }
}
