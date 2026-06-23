using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Styling;
using Avalonia.Threading;
using BorgMate.Domain.Settings;
using BorgMate.Infrastructure.Platform;
using BorgMate.Persistence.Configuration;
using BorgMate.Persistence.Journal;
using BorgMate.Presentation.Dialogs;
using BorgMate.Presentation.State;
using BorgMate.Presentation.Views;
using BorgMate.Runtime.Jobs;
using BorgMate.Runtime.Scheduling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BorgMate.Presentation.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly IAutoStartService _autoStartService = null!;
    private readonly DispatcherTimer _elapsedTimer = null!;
    private readonly IJournalStore _journalService = null!;
    private readonly IRepositoryConfigStore _repositoryConfigStore = null!;
    private readonly IBackupScheduler _scheduler = null!;
    private readonly AppSettings _settings = null!;
    private readonly ISettingsStore _settingsStore = null!;
    private readonly RepositoryPresentationState _store = null!;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRepositoriesPage))]
    [NotifyPropertyChangedFor(nameof(IsNotificationsPage))]
    private AppPage _activePage = AppPage.Repositories;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SidebarWidth))]
    private bool _isSidebarExpanded;

    [ObservableProperty]
    private int _pendingJobCount;

    private int _scheduleRefreshCounter;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedJob))]
    private BorgJob? _selectedJob;

    public MainWindowViewModel()
    {
    }

    public MainWindowViewModel(
        ISettingsStore settingsStore,
        IRepositoryConfigStore repositoryConfigStore,
        AppSettings settings,
        RepositoriesPageViewModel repositoriesPage,
        NotificationsViewModel notifications,
        IJournalStore journalService,
        UpdateStatusState status,
        IJobController jobController,
        RepositoryPresentationState store,
        IAutoStartService autoStartService,
        IBackupScheduler scheduler)
    {
        _settingsStore = settingsStore;
        _repositoryConfigStore = repositoryConfigStore;
        _settings = settings;
        _autoStartService = autoStartService;
        _journalService = journalService;
        _store = store;
        RepositoriesPage = repositoriesPage;
        Notifications = notifications;
        Status = status;
        JobController = jobController;
        _scheduler = scheduler;

        _elapsedTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _elapsedTimer.Tick += (_, _) => OnTimerTick();
        _elapsedTimer.Start();

        RepositoriesPage.IsActive = true;

        LoadConfig();
        _scheduler.Start();
    }

    public RepositoriesPageViewModel RepositoriesPage { get; } = null!;
    public NotificationsViewModel Notifications { get; } = null!;
    public UpdateStatusState Status { get; } = null!;
    public IJobController JobController { get; } = null!;

    public bool HasSelectedJob => SelectedJob is not null;

    public double SidebarWidth => IsSidebarExpanded ? 200 : 48;

    public bool IsRepositoriesPage => ActivePage == AppPage.Repositories;
    public bool IsNotificationsPage => ActivePage == AppPage.Notifications;

    private void OnTimerTick()
    {
        var snapshot = JobController.GetSnapshot();
        PendingJobCount = snapshot.PendingCount;
        if (snapshot.HasRunningJobs)
            JobController.UpdateRunningElapsed();
        if (JobController.ConsumeQueryInvalidated()) RepositoriesPage.InvalidateArchives();
        double? runningProgress = null;
        foreach (var repo in _store.Repositories)
        {
            if (!repo.IsBusy)
            {
                repo.CommandProgress = null;
                continue;
            }

            var job = snapshot.Jobs.FirstOrDefault(j =>
                j.RepoPath == repo.Path && j.Kind == BorgJobKind.Command && j.Status == BorgJobStatus.Running);
            repo.CommandProgress = job?.Progress;
            runningProgress ??= job?.Progress;
        }

        App.UpdateRunningIndicator(snapshot.HasRunningJobs, PendingJobCount, runningProgress);

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
    private async Task OpenAbout()
    {
        var window = new AboutWindow();
        var parent = DialogHelper.GetMainWindow();
        if (parent is not null)
            await window.ShowDialog(parent);
    }

    [RelayCommand]
    private async Task OpenAppSettings()
    {
        var vm = new AppSettingsViewModel(_settings, _settingsStore, _autoStartService);
        var window = new AppSettingsWindow { DataContext = vm };
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
    private void NavigateTo(AppPage page)
    {
        ActivePage = page;
    }

    [RelayCommand]
    private void ClearCompletedJobs()
    {
        JobController.ClearCompleted();
    }


    private void LoadConfig()
    {
        IsSidebarExpanded = _settings.SidebarExpanded;
        if (Application.Current is not null)
            Application.Current.RequestedThemeVariant = _settings.Theme switch
            {
                AppTheme.Light => ThemeVariant.Light,
                AppTheme.Dark => ThemeVariant.Dark,
                _ => ThemeVariant.Default
            };

        foreach (var repo in _repositoryConfigStore.GetSnapshot().Repositories)
            _store.Add(repo);
    }

    /// <summary>Saves shell, repository, and settings state without blocking the UI thread.</summary>
    public async Task SaveConfigAsync()
    {
        _settings.SidebarExpanded = IsSidebarExpanded;

        foreach (var repo in _store.Repositories.ToList())
            await _repositoryConfigStore.UpdateAsync(repo);

        await _settingsStore.SaveAsync();
    }
}
