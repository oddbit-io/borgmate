using BorgMate.Models;
using BorgMate.Services;
using BorgMate.Services.Borg;
using BorgMate.Services.AutoStart;
using BorgMate.Services.Config;
using BorgMate.Services.Journal;
using BorgMate.Services.Keychain;
using BorgMate.Services.Queue;
using BorgMate.Services.UI;
using BorgMate.ViewModels;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace BorgMate.Tests;

public class MainWindowViewModelTests : IDisposable
{
    private static readonly WslHelper Wsl = new(Substitute.For<ILogger<WslHelper>>());
    private static readonly SshAgentHelper SshAgent = new(Substitute.For<ILogger<SshAgentHelper>>(), null, Wsl);

    private readonly IConfigService _configService = Substitute.For<IConfigService>();
    private readonly AppSettings _settings = new();
    private readonly IJournalService _journalService;
    private readonly JobQueueService _jobQueue = new();
    private readonly RepositoryStore _store;
    private readonly ISchedulerService _scheduler = Substitute.For<ISchedulerService>();
    private readonly IAutoStartService _autoStart = Substitute.For<IAutoStartService>();

    public MainWindowViewModelTests()
    {
        _journalService = Substitute.For<IJournalService>();
        _journalService.Entries.Returns(new System.Collections.ObjectModel.ObservableCollection<JournalEntry>());
        _configService.Load().Returns(new ConfigData());
        _store = new RepositoryStore(_jobQueue);
    }

    private RepositoriesPageViewModel CreatePageVm()
    {
        var borgFactory = new BorgServiceFactory(Substitute.For<ILoggerFactory>(), _settings, SshAgent, Wsl);
        var passphrase = new PassphrasePrompt(null);
        var runner = new BorgOperationRunner(
            Substitute.For<ILogger<BorgOperationRunner>>(), _jobQueue, _journalService, passphrase);
        var sizeCalculator = new DirectorySizeCalculator(Substitute.For<ILogger<DirectorySizeCalculator>>());
        return new RepositoriesPageViewModel(_settings, borgFactory, _configService, new FilePickerService(),
            new BorgCacheService(), _journalService, runner, passphrase, Substitute.For<IKeychainService>(), Wsl, sizeCalculator,
            null!, _store, Substitute.For<ILogger<RepositoriesPageViewModel>>());
    }

    public void Dispose()
    {
        _store.Dispose();
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
        var vm = new MainWindowViewModel(_configService, _settings, page,
            notifications, _journalService, new StatusService(), _jobQueue, _store, _autoStart, _scheduler);

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

    // --- SaveConfig ---

    [Fact]
    public void SaveConfig_PersistsSidebarState()
    {
        _configService.Load().Returns(new ConfigData());
        var page = CreatePageVm();
        var notifications = new NotificationsViewModel(_journalService);

        var vm = new MainWindowViewModel(_configService, _settings, page,
            notifications, _journalService, new StatusService(), _jobQueue, _store, _autoStart, _scheduler);

        vm.IsSidebarExpanded = true;
        vm.SaveConfig();

        Assert.True(_settings.SidebarExpanded);
        _configService.Received().Save(Arg.Is<ConfigData>(c => c.Settings.SidebarExpanded));
    }

    [Fact]
    public void Constructor_LoadsRepositoriesFromConfig()
    {
        var configData = new ConfigData
        {
            Repositories =
            [
                new RepositoryData { Name = "repo1", Path = "/repo1" },
                new RepositoryData { Name = "repo2", Path = "/repo2" }
            ]
        };
        _configService.Load().Returns(configData);
        var page = CreatePageVm();
        var notifications = new NotificationsViewModel(_journalService);

        var vm = new MainWindowViewModel(_configService, _settings, page,
            notifications, _journalService, new StatusService(), _jobQueue, _store, _autoStart, _scheduler);

        Assert.Equal(2, _store.Repositories.Count);
        Assert.Equal("repo1", _store.Repositories[0].Name);
        Assert.Equal("repo2", _store.Repositories[1].Name);
    }
}
