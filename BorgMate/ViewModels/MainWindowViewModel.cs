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
    private readonly DispatcherTimer _elapsedTimer = null!;

    public RepositoryListViewModel RepositoryList { get; } = null!;
    public ArchiveListViewModel ArchiveList { get; } = null!;
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
    private bool _isRepositorySelected;

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
        RepositoryListViewModel repositoryList,
        ArchiveListViewModel restore,
        NotificationsViewModel activity,
        IJournalService journalService,
        StatusService status,
        JobQueueService jobQueue,
        IAutoStartService autoStartService,
        ISchedulerService scheduler)
    {
        _configService = configService;
        _settings = settings;
        _autoStartService = autoStartService;
        _journalService = journalService;
        RepositoryList = repositoryList;
        ArchiveList = restore;
        Notifications = activity;
        Status = status;
        JobQueue = jobQueue;
        _scheduler = scheduler;

        _elapsedTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _elapsedTimer.Tick += (_, _) => OnTimerTick();
        _elapsedTimer.Start();

        _configService.SaveRequested += SaveConfig;
        RepositoryList.ArchivesChanged += () => ArchiveList.InvalidateArchives();
        RepositoryList.BackupCompleted += repoPath =>
        {
            if (RepositoryList.SelectedRepository?.Path == repoPath)
                ArchiveList.InvalidateArchives();
        };

        RepositoryList.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(RepositoryListViewModel.SelectedRepository))
            {
                var repo = RepositoryList.SelectedRepository;
                IsRepositorySelected = repo is not null;
                ArchiveList.Repository = repo;
                ArchiveList.IsActive = repo is not null;
                if (repo is not null)
                    RepositoryList.FetchStatsCommand.ExecuteAsync(null);
            }
        };

        LoadConfig();
        _scheduler.Start(RepositoryList);
    }

    private int _scheduleRefreshCounter;

    /// <summary>
    /// Polls every second: updates job counts and elapsed time, checks for query invalidation,
    /// refreshes tray/dock running indicators, and refreshes schedule display every 30 ticks.
    /// </summary>
    private void OnTimerTick()
    {
        PendingJobCount = JobQueue.PendingCount;
        if (JobQueue.HasRunningJobs)
            JobQueue.UpdateRunningElapsed();
        if (JobQueue.ConsumeQueryInvalidated())
        {
            ArchiveList.InvalidateArchives();
            if (RepositoryList.SelectedRepository is not null)
                RepositoryList.FetchStatsCommand.ExecuteAsync(null);
        }
        var runningProgress = JobQueue.HasRunningJobs
            ? JobQueue.Jobs.FirstOrDefault(j => j.Status == BorgJobStatus.Running && j.Kind == BorgJobKind.Command)?.Progress
            : null;
        App.UpdateRunningIndicator(JobQueue.HasRunningJobs, PendingJobCount, runningProgress);

        if (++_scheduleRefreshCounter >= 30)
        {
            _scheduleRefreshCounter = 0;
            foreach (var repo in RepositoryList.Repositories)
                repo.RefreshScheduleDisplay();
        }
    }

    partial void OnActivePageChanged(AppPage value)
    {
        ArchiveList.IsActive = value == AppPage.Repositories && IsRepositorySelected;
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


    /// <summary>
    /// Loads config and populates repositories. Migrates legacy BackupTaskData
    /// entries to BorgRepository source directories on first load.
    /// </summary>
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
            RepositoryList.Repositories.Add(ConfigService.ToModel(repoData));

        // Migrate legacy tasks to repos (copies source dirs and schedule)
        foreach (var taskData in config.Tasks)
        {
            var repo = RepositoryList.Repositories.FirstOrDefault(r => r.Path == taskData.RepositoryPath);
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
            Repositories = RepositoryList.Repositories
                .Select(ConfigService.FromModel)
                .ToList()
        };

        _configService.Save(config);
    }
}
