using System;
using System.Linq;
using Avalonia;
using Avalonia.Styling;
using Avalonia.Threading;
using BorgMate.Models;
using BorgMate.Services;
using BorgMate.Services.Config;
using BorgMate.Services.Journal;
using BorgMate.Services.Queue;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BorgMate.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly IConfigService _configService = null!;
    private readonly AppSettings _settings = null!;
    private readonly IAutoStartService _autoStartService = null!;
    private readonly IJournalService _journalService = null!;
    private readonly RepositoryStore _store = null!;
    private readonly DispatcherTimer _elapsedTimer = null!;

    public RepositoriesPageViewModel RepositoriesPage { get; } = null!;
    public NotificationsViewModel Notifications { get; } = null!;
    public StatusService Status { get; } = null!;
    public JobQueueService JobQueue { get; } = null!;
    private readonly ISchedulerService _scheduler = null!;

    [ObservableProperty]
    private int _pendingJobCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedJob))]
    private BorgJob? _selectedJob;

    public bool HasSelectedJob => SelectedJob is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SidebarWidth))]
    private bool _isSidebarExpanded;

    public double SidebarWidth => IsSidebarExpanded ? 200 : 48;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRepositoriesPage))]
    [NotifyPropertyChangedFor(nameof(IsNotificationsPage))]
    private AppPage _activePage = AppPage.Repositories;

    public bool IsRepositoriesPage => ActivePage == AppPage.Repositories;
    public bool IsNotificationsPage => ActivePage == AppPage.Notifications;

    public MainWindowViewModel() {}

    public MainWindowViewModel(
        IConfigService configService,
        AppSettings settings,
        RepositoriesPageViewModel repositoriesPage,
        NotificationsViewModel notifications,
        IJournalService journalService,
        StatusService status,
        JobQueueService jobQueue,
        RepositoryStore store,
        IAutoStartService autoStartService,
        ISchedulerService scheduler)
    {
        _configService = configService;
        _settings = settings;
        _autoStartService = autoStartService;
        _journalService = journalService;
        _store = store;
        RepositoriesPage = repositoriesPage;
        Notifications = notifications;
        Status = status;
        JobQueue = jobQueue;
        _scheduler = scheduler;

        _elapsedTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _elapsedTimer.Tick += (_, _) => OnTimerTick();
        _elapsedTimer.Start();

        _configService.SaveRequested += SaveConfig;
        RepositoriesPage.IsActive = true;

        LoadConfig();
        _scheduler.Start(RepositoriesPage);
    }

    private int _scheduleRefreshCounter;

    private void OnTimerTick()
    {
        PendingJobCount = JobQueue.PendingCount;
        if (JobQueue.HasRunningJobs)
            JobQueue.UpdateRunningElapsed();
        if (JobQueue.ConsumeQueryInvalidated())
        {
            RepositoriesPage.InvalidateArchives();
        }
        double? runningProgress = null;
        foreach (var repo in _store.Repositories)
        {
            if (!repo.IsBusy) { repo.CommandProgress = null; continue; }
            var job = JobQueue.Jobs.FirstOrDefault(j =>
                j.RepoPath == repo.Path && j.Kind == BorgJobKind.Command && j.Status == BorgJobStatus.Running);
            repo.CommandProgress = job?.Progress;
            runningProgress ??= job?.Progress;
        }
        App.UpdateRunningIndicator(JobQueue.HasRunningJobs, PendingJobCount, runningProgress);

        if (++_scheduleRefreshCounter >= 30)
        {
            _scheduleRefreshCounter = 0;
            foreach (var repo in _store.Repositories)
                repo.RefreshScheduleDisplay();
        }
    }

    partial void OnActivePageChanged(AppPage value)
    {
        RepositoriesPage.IsActive = value == AppPage.Repositories;
        _journalService.IsActive = value == AppPage.Notifications;
        if (value == AppPage.Notifications)
            _journalService.MarkAllRead();
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task OpenAbout()
    {
        var window = new Views.AboutWindow();
        var parent = DialogHelper.GetMainWindow();
        if (parent is not null)
            await window.ShowDialog(parent);
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task OpenAppSettings()
    {
        var vm = new AppSettingsViewModel(_settings, _configService, _autoStartService);
        var window = new Views.AppSettingsWindow { DataContext = vm };
        var parent = DialogHelper.GetMainWindow();
        if (parent is not null)
            await window.ShowDialog(parent);
    }

    [RelayCommand]
    private void ToggleSidebar()
    {
        IsSidebarExpanded = !IsSidebarExpanded;
    }

    [RelayCommand]
    private void NavigateTo(AppPage page) => ActivePage = page;

    [RelayCommand]
    private void ClearCompletedJobs() => JobQueue.ClearCompleted();


    private void LoadConfig()
    {
        var config = _configService.Load();
        IsSidebarExpanded = _settings.SidebarExpanded;
        if (Application.Current is not null)
        {
            Application.Current.RequestedThemeVariant = _settings.Theme switch
            {
                AppTheme.Light => ThemeVariant.Light,
                AppTheme.Dark => ThemeVariant.Dark,
                _ => ThemeVariant.Default
            };
        }

        foreach (var repoData in config.Repositories)
            _store.Add(ConfigService.ToModel(repoData));

        // Migrate legacy tasks to repos (copies source dirs and schedule)
        foreach (var taskData in config.Tasks)
        {
            var repo = _store.FindByPath(taskData.RepositoryPath);
            if (repo is not null && repo.SourceDirectories.Count == 0 && taskData.SourceDirectories.Count > 0)
            {
                foreach (var dir in taskData.SourceDirectories)
                    repo.SourceDirectories.Add(dir);
            }
        }
    }

    public void SaveConfig()
    {
        _settings.SidebarExpanded = IsSidebarExpanded;

        var config = new ConfigData
        {
            Settings = _settings,
            Repositories = _store.Repositories
                .Select(ConfigService.FromModel)
                .ToList()
        };

        _configService.Save(config);
    }
}
