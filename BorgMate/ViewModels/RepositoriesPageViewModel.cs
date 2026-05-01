using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using BorgMate.Localization;
using BorgMate.Models;
using BorgMate.Services;
using BorgMate.Services.Borg;
using BorgMate.Services.Config;
using BorgMate.Services.Journal;
using BorgMate.Services.Queue;
using BorgMate.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace BorgMate.ViewModels;

/// <summary>Single VM for the entire Repositories page (left list + right archives).</summary>
public partial class RepositoriesPageViewModel : ViewModelBase
{
    private readonly AppSettings _settings = null!;
    private readonly BorgServiceFactory _borgServiceFactory = null!;
    private readonly IConfigService _configService = null!;
    private readonly IFilePickerService _filePicker = null!;
    private readonly BorgCacheService _cache = null!;
    private readonly IJournalService _journalService = null!;
    private readonly BorgOperationRunner _runner = null!;
    private readonly PassphrasePrompt _passphrase = null!;
    private readonly WslHelper _wsl = null!;
    private readonly DirectorySizeCalculator _sizeCalculator = null!;
    private readonly JobQueueService? _jobQueue;
    private readonly RepositoryStore _store = null!;
    private readonly ILogger<RepositoriesPageViewModel> _logger = null!;

    // === Selection (forwarded to store) ===

    public ObservableCollection<BorgRepository> Repositories => _store.Repositories;

    public BorgRepository? SelectedRepository
    {
        get => _store.SelectedRepository;
        set => _store.SelectedRepository = value;
    }

    private BorgRepository? _previousSelection;

    // === Selected archive (presentation state) ===

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedArchive))]
    [NotifyPropertyChangedFor(nameof(CanModifyArchive))]
    [NotifyCanExecuteChangedFor(nameof(DeleteArchiveCommand))]
    [NotifyCanExecuteChangedFor(nameof(PickAndRestoreCommand))]
    [NotifyCanExecuteChangedFor(nameof(BrowseAndRestoreCommand))]
    private BorgArchive? _selectedArchive;

    public bool HasSelectedArchive => SelectedArchive is not null;

    // === Computed selection state ===

    public bool CanEditOrRemove => SelectedRepository is not null && !SelectedRepository.IsBusy;
    public bool CanRunBackup => SelectedRepository is not null && !SelectedRepository.IsBusy && SelectedRepository.SourceDirectories.Count > 0;
    public bool CanModifyArchive => SelectedArchive is not null && SelectedRepository is { IsBusy: false, HasError: false };
    public bool IsRepoIdle => SelectedRepository is null or { IsBusy: false, HasError: false };
    public bool IsRepositorySelected => SelectedRepository is not null;
    public bool SelectedRepoHasError => SelectedRepository?.HasError == true;
    public string? SelectedRepoLastError => SelectedRepository?.LastError;
    public bool ShowArchiveList => IsRepositorySelected && !SelectedRepoHasError;

    /// <summary>Forwards <c>AppSettings.AutoLoadArchives</c> for view bindings.</summary>
    public bool AutoLoadArchives => _settings?.AutoLoadArchives ?? true;

    /// <summary>Forwards <c>AppSettings.AutoLoadStats</c> for view bindings.</summary>
    public bool AutoLoadStats => _settings?.AutoLoadStats ?? true;

    /// <summary>Forwards <c>AppSettings.AutoLoadArchiveDetails</c> for view bindings.</summary>
    public bool AutoLoadArchiveDetails => _settings?.AutoLoadArchiveDetails ?? true;

    /// <summary>
    /// Visible when AutoLoadArchives is off, the list is empty, and we are
    /// not currently fetching — prompts the user to click Refresh.
    /// </summary>
    public bool ShowArchiveListPlaceholder =>
        !AutoLoadArchives &&
        SelectedRepository is { IsBusy: false, HasError: false } &&
        !IsArchiveBusy &&
        Archives.Count == 0;

    // === Page activation (set from MainWindowViewModel based on ActivePage) ===

    [ObservableProperty]
    private bool _isActive;

    // === Loading flags ===

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsArchiveBusy))]
    [NotifyPropertyChangedFor(nameof(ShowArchiveListPlaceholder))]
    private bool _isLoadingArchives;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsArchiveBusy))]
    [NotifyPropertyChangedFor(nameof(ShowArchiveListPlaceholder))]
    private bool _isDeletingArchive;

    public bool IsArchiveBusy => IsLoadingArchives || IsDeletingArchive;

    // === Sort state ===

    private enum SortField { Date, Name }
    private SortField _sortField = SortField.Date;
    private bool _sortAscending;

    [ObservableProperty]
    private string _nameSortIndicator = "";

    [ObservableProperty]
    private string _dateSortIndicator = " ▼";

    // === Archive detail state ===

    [ObservableProperty]
    private string? _detailOriginalSize;

    [ObservableProperty]
    private string? _detailFileCount;

    public bool HasDetail => DetailOriginalSize is not null;

    // === Restore path ===

    [ObservableProperty]
    private string _restorePath = string.Empty;

    // === Collections ===

    public ObservableCollection<BorgArchive> Archives { get; } = [];

    // === Trackers ===

    public JobProgressTracker Progress { get; } = new(confirmCancel: true);
    public JobProgressTracker RestoreProgress { get; } = new(confirmCancel: true);

    // === Constructors ===

    public RepositoriesPageViewModel()
    {
        _store = new RepositoryStore(new JobQueueService());
    }

    public RepositoriesPageViewModel(
        AppSettings settings,
        BorgServiceFactory borgServiceFactory,
        IConfigService configService,
        IFilePickerService filePicker,
        BorgCacheService cache,
        IJournalService journalService,
        BorgOperationRunner runner,
        PassphrasePrompt passphrase,
        WslHelper wsl,
        DirectorySizeCalculator sizeCalculator,
        JobQueueService jobQueue,
        RepositoryStore store,
        ILogger<RepositoriesPageViewModel> logger)
    {
        _settings = settings;
        _borgServiceFactory = borgServiceFactory;
        _configService = configService;
        _filePicker = filePicker;
        _cache = cache;
        _journalService = journalService;
        _runner = runner;
        _passphrase = passphrase;
        _wsl = wsl;
        _sizeCalculator = sizeCalculator;
        _jobQueue = jobQueue;
        _store = store;
        _logger = logger;

        _store.PropertyChanged += OnStorePropertyChanged;
        _store.SelectedLoadingStateChanged += () =>
            IsLoadingArchives = _store.SelectedRepository is { } r && _store.IsLoadingArchives(r);

        Strings.LanguageChanged += () =>
        {
            foreach (var repo in Repositories)
            {
                repo.RefreshScheduleDisplay();
                repo.RefreshStats();
            }
        };
        Strings.LanguageChanged += ReformatDetail;
        Strings.LanguageChanged += RefreshArchiveDates;

        Archives.CollectionChanged += (_, _) => OnPropertyChanged(nameof(ShowArchiveListPlaceholder));

        _configService.SaveRequested += OnSettingsMaybeChanged;
    }

    /// <summary>
    /// Re-broadcasts the AutoLoad* getters whenever any save request fires. Cheap and
    /// covers settings-dialog edits without an additional event channel — view bindings
    /// to <see cref="AutoLoadArchives"/>, <see cref="AutoLoadStats"/>, etc. refresh.
    /// </summary>
    private void OnSettingsMaybeChanged()
    {
        OnPropertyChanged(nameof(AutoLoadArchives));
        OnPropertyChanged(nameof(AutoLoadStats));
        OnPropertyChanged(nameof(AutoLoadArchiveDetails));
        OnPropertyChanged(nameof(ShowArchiveListPlaceholder));
    }

    // === Selection / store handlers ===

    private void OnStorePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(RepositoryStore.SelectedRepository)) return;

        var oldRepo = _previousSelection;
        var newRepo = _store.SelectedRepository;
        _previousSelection = newRepo;

        if (oldRepo is not null) oldRepo.PropertyChanged -= OnSelectedRepoPropertyChanged;
        if (newRepo is not null) newRepo.PropertyChanged += OnSelectedRepoPropertyChanged;

        // Save display-order archives back to the repo before switching
        if (oldRepo is not null && Archives.Count > 0)
            oldRepo.LoadedArchives = Archives.ToList();

        Archives.Clear();
        SelectedArchive = null;
        IsLoadingArchives = newRepo is not null && _store.IsLoadingArchives(newRepo);

        OnPropertyChanged(nameof(SelectedRepository));
        OnPropertyChanged(nameof(IsRepositorySelected));
        OnPropertyChanged(nameof(SelectedRepoHasError));
        OnPropertyChanged(nameof(SelectedRepoLastError));
        OnPropertyChanged(nameof(ShowArchiveList));
        OnPropertyChanged(nameof(CanEditOrRemove));
        OnPropertyChanged(nameof(CanRunBackup));
        OnPropertyChanged(nameof(CanModifyArchive));
        OnPropertyChanged(nameof(IsRepoIdle));
        RefreshArchiveCommandStates();

        if (newRepo is null)
        {
            UpdateActiveJob();
            return;
        }

        if (newRepo.LoadedArchives is { } archives)
        {
            Archives.ReplaceWith(archives);
        }
        else if (IsActive && !newRepo.IsBusy && !newRepo.HasError && _settings.AutoLoadArchives)
        {
            ListArchivesCommand.ExecuteAsync(null);
        }

        if (!newRepo.HasError && !newRepo.HasStats && _settings.AutoLoadStats)
            FetchStatsCommand.ExecuteAsync(null);

        OnPropertyChanged(nameof(ShowArchiveListPlaceholder));

        UpdateActiveJob();
    }

    private void OnSelectedRepoPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(BorgRepository.IsBusy) or nameof(BorgRepository.HasError))
        {
            OnPropertyChanged(nameof(CanEditOrRemove));
            OnPropertyChanged(nameof(CanRunBackup));
            OnPropertyChanged(nameof(CanModifyArchive));
            OnPropertyChanged(nameof(IsRepoIdle));
            OnPropertyChanged(nameof(SelectedRepoHasError));
            OnPropertyChanged(nameof(SelectedRepoLastError));
            OnPropertyChanged(nameof(ShowArchiveList));
            OnPropertyChanged(nameof(ShowArchiveListPlaceholder));
            RefreshArchiveCommandStates();
            if (e.PropertyName == nameof(BorgRepository.IsBusy))
                UpdateActiveJob();
        }

        if (e.PropertyName == nameof(BorgRepository.LastError))
            OnPropertyChanged(nameof(SelectedRepoLastError));

        // Recovery: when HasError clears, refetch the archive list if empty.
        if (e.PropertyName == nameof(BorgRepository.HasError) &&
            _store.SelectedRepository is { HasError: false } &&
            Archives.Count == 0)
        {
            Dispatcher.UIThread.Post(FetchArchivesIfEmpty);
        }
    }

    partial void OnIsActiveChanged(bool value)
    {
        if (value && _settings.AutoLoadArchives && _store.SelectedRepository is { IsBusy: false, HasError: false } && Archives.Count == 0)
            ListArchivesCommand.ExecuteAsync(null);
    }

    public void InvalidateArchives()
    {
        if (_store.SelectedRepository is { } repo)
        {
            repo.LoadedArchives = null;
            _cache.InvalidateRepo(repo.Path);
        }
        Archives.Clear();
        SelectedArchive = null;
        if (IsActive && _settings.AutoLoadArchives && _store.SelectedRepository is { IsBusy: false, HasError: false })
            ListArchivesCommand.ExecuteAsync(null);
        OnPropertyChanged(nameof(ShowArchiveListPlaceholder));
    }

    public void FetchArchivesIfEmpty()
    {
        if (Archives.Count == 0 && IsActive && _settings.AutoLoadArchives && _store.SelectedRepository is { IsBusy: false, HasError: false })
            ListArchivesCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private void RetryRepo()
    {
        if (SelectedRepository is not { } repo) return;
        repo.HasError = false;
        repo.LastError = null;
        repo.LoadedArchives = null;
        repo.StatsTotalSize = null;
        repo.StatsTotalChunks = null;
        FetchArchivesIfEmpty();
        if (!repo.HasStats)
            FetchStatsCommand.ExecuteAsync(null);
    }

    private void RefreshArchiveCommandStates()
    {
        DeleteArchiveCommand.NotifyCanExecuteChanged();
        PickAndRestoreCommand.NotifyCanExecuteChanged();
        BrowseAndRestoreCommand.NotifyCanExecuteChanged();
    }

    // === Repo CRUD ===

    private async Task ShowEditorAsync(RepositoryEditorViewModel vm)
    {
        var window = new RepositoryEditorWindow { DataContext = vm };
        var parent = DialogHelper.GetMainWindow();
        if (parent is not null)
            await window.ShowDialog(parent);
    }

    [RelayCommand]
    private async Task AddNew()
    {
        var vm = RepositoryEditorViewModel.ForNew(
            _borgServiceFactory, _filePicker, _jobQueue, _journalService, _passphrase, _runner, _wsl);
        await ShowEditorAsync(vm);

        if (vm.IsSaved && vm.Repository is { } repo)
        {
            _store.Add(repo);
            SelectedRepository = repo;
            _configService.RequestSave();
        }
    }

    [RelayCommand]
    private async Task OpenExisting()
    {
        var vm = RepositoryEditorViewModel.ForOpen(
            _borgServiceFactory, _filePicker, _jobQueue, _journalService, _passphrase, _runner, _wsl);
        await ShowEditorAsync(vm);

        if (vm.IsSaved && vm.Repository is { } repo)
        {
            _store.Add(repo);
            SelectedRepository = repo;
            _configService.RequestSave();
        }
    }

    [RelayCommand]
    private async Task EditRepository(BorgRepository repo)
    {
        _jobQueue?.CancelQueryByRepoPath(repo.Path);

        var vm = RepositoryEditorViewModel.ForEdit(
            _borgServiceFactory, _filePicker, repo, _jobQueue, _journalService, _passphrase, _runner, _wsl);
        await ShowEditorAsync(vm);

        if (vm.IsSaved)
        {
            await CopySshKeyIfNeeded(repo);
            _configService.RequestSave();
            OnPropertyChanged(nameof(CanRunBackup));

            if (SelectedRepository == repo)
            {
                InvalidateArchives();
                if (_settings.AutoLoadStats)
                    _ = FetchStatsCommand.ExecuteAsync(null);
            }
        }
        else if (SelectedRepository == repo)
        {
            if (SelectedRepository is { HasStats: false } && _settings.AutoLoadStats)
                _ = FetchStatsCommand.ExecuteAsync(null);
            FetchArchivesIfEmpty();
        }
    }

    [RelayCommand]
    private async Task DuplicateRepository(BorgRepository repo)
    {
        if (repo is null) return;

        var copyName = GenerateUniqueCopyName(repo.Name);
        var vm = RepositoryEditorViewModel.ForDuplicate(
            _borgServiceFactory, _filePicker, repo, copyName,
            _jobQueue, _journalService, _passphrase, _runner, _wsl);
        await ShowEditorAsync(vm);

        if (vm.IsSaved && vm.Repository is { } newRepo)
        {
            _store.Add(newRepo);
            SelectedRepository = newRepo;
            _configService.RequestSave();
        }
    }

    internal string GenerateUniqueCopyName(string baseName)
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

    [RelayCommand]
    private async Task RemoveRepository(BorgRepository repo)
    {
        if (!await DialogHelper.ConfirmAsync(string.Format(Strings.Get("ConfirmDeleteRepo"), repo.Name)))
            return;

        _store.Remove(repo);
        _configService.RequestSave();
    }

    // === Backup ===

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task RunBackup()
    {
        if (SelectedRepository is { } repo && repo.SourceDirectories.Count > 0)
            await RunBackupForRepo(repo);
    }

    public async Task RunBackupForRepo(BorgRepository repo)
    {
        if (repo.SourceDirectories.Count == 0) return;

        var prefix = string.IsNullOrWhiteSpace(repo.ArchiveNamePrefix) ? repo.Name : repo.ArchiveNamePrefix;
        var archiveName = $"{prefix}-{DateTime.Now:yyyy-MM-ddTHH-mm-ss}";
        var service = _borgServiceFactory.GetService(repo.BorgVersion);

        var result = await RunCommandAsync(repo, JournalEventKind.Backup,
            jobName: $"{Strings.Get("Job.Backup")}: {repo.Name}",
            execute: async (j, ct) =>
            {
                Dispatcher.UIThread.Post(() => j.StatusMessage = Strings.Get("Status.CalculatingSize"));
                var (totalSize, totalFiles) = _sizeCalculator.Calculate(repo.SourceDirectories, ct, (dirs, files, size) =>
                {
                    var msg = string.Format(Strings.Get("Status.CalculatingSizeProgress"),
                        dirs.ToString("N0", Strings.Culture),
                        files.ToString("N0", Strings.Culture),
                        Strings.FormatBytes(size));
                    Dispatcher.UIThread.Post(() => j.StatusMessage = msg);
                });
                if (ct.IsCancellationRequested)
                    return new BorgResult(-1, "", "", WasCancelled: true);
                j.TotalSize = totalSize;
                j.TotalFileCount = totalFiles;

                return await _runner.RunWithTransientRetry(j,
                    () => service.CreateBackupAsync(repo, archiveName, repo.SourceDirectories, ct,
                        onStderrLine: line => BorgProgressParser.Update(j, line)));
            },
            onJobCreated: job => { if (SelectedRepository == repo) Progress.SetActiveJob(job); },
            invalidateArchivesOnSuccess: true);

        if (result.Success)
        {
            repo.LastBackupAt = DateTime.Now;
            _configService.RequestSave();
            if (SelectedRepository == repo)
                _ = FetchStatsCommand.ExecuteAsync(null);

            if (repo.Schedule.RunPruneAfterBackup && repo.PruneOptions.HasAnyRetention)
                await RunPruneForRepo(repo);
        }
    }

    // === Maintenance (prune / check / compact) ===

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task PruneRepo()
    {
        if (SelectedRepository is { } repo)
            await RunPruneForRepo(repo, confirm: true);
    }

    private async Task RunPruneForRepo(BorgRepository repo, bool confirm = false)
    {
        var service = _borgServiceFactory.GetService(repo.BorgVersion);

        var result = await RunCommandAsync(repo, JournalEventKind.Prune,
            jobName: $"{Strings.Get("Job.Prune")}: {repo.Name}",
            execute: (j, ct) =>
            {
                j.StatusMessage = string.Format(Strings.Get("Status.Pruning"), repo.Name);
                return _runner.RunWithTransientRetry(j, () => service.PruneAsync(repo, ct));
            },
            confirmMessage: confirm ? string.Format(Strings.Get("ConfirmPrune"), repo.Name) : null,
            onJobCreated: job => { if (SelectedRepository == repo) Progress.SetActiveJob(job); },
            invalidateArchivesOnSuccess: true);

        if (result.Success && repo.PruneOptions.CompactAfterPrune)
            await RunCompactForRepo(repo);
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task CheckRepo()
    {
        if (SelectedRepository is not { } repo) return;
        var service = _borgServiceFactory.GetService(repo.BorgVersion);

        await RunCommandAsync(repo, JournalEventKind.Check,
            jobName: $"{Strings.Get("Job.Check")}: {repo.Name}",
            execute: (j, ct) =>
            {
                j.ProgressLabel = Strings.Get("Status.CheckingProgress");
                j.StatusMessage = j.ProgressLabel;
                return _runner.RunWithTransientRetry(j,
                    () => service.CheckAsync(repo, ct, onStderrLine: line => BorgProgressParser.Update(j, line)));
            },
            onJobCreated: job => { if (SelectedRepository == repo) Progress.SetActiveJob(job); });
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task CompactRepo()
    {
        if (SelectedRepository is { } repo)
            await RunCompactForRepo(repo, confirm: true);
    }

    private async Task RunCompactForRepo(BorgRepository repo, bool confirm = false)
    {
        var service = _borgServiceFactory.GetService(repo.BorgVersion);

        var result = await RunCommandAsync(repo, JournalEventKind.Compact,
            jobName: $"{Strings.Get("Job.Compact")}: {repo.Name}",
            execute: (j, ct) =>
            {
                j.ProgressLabel = Strings.Get("Status.CompactingProgress");
                j.StatusMessage = j.ProgressLabel;
                return _runner.RunWithTransientRetry(j,
                    () => service.CompactAsync(repo, ct, onStderrLine: line => BorgProgressParser.Update(j, line)));
            },
            confirmMessage: confirm ? string.Format(Strings.Get("ConfirmCompact"), repo.Name) : null,
            onJobCreated: job => { if (SelectedRepository == repo) Progress.SetActiveJob(job); },
            invalidateArchivesOnSuccess: true);

        if (result.Success && SelectedRepository == repo)
            _ = FetchStatsCommand.ExecuteAsync(null);
    }

    // === Active job tracking for the detail panel ===

    /// <summary>
    /// Re-binds Progress / RestoreProgress to whatever job is in flight for the
    /// selected repo. Called on selection change so the detail panel doesn't go
    /// blank when re-selecting a busy repo.
    /// </summary>
    private void UpdateActiveJob()
    {
        Progress.SetActiveJob(null);
        RestoreProgress.SetActiveJob(null);

        if (_store.SelectedRepository is not { IsBusy: true } repo || _jobQueue is null)
            return;

        foreach (var job in _jobQueue.Jobs)
        {
            if (job.RepoPath != repo.Path || job.Kind != BorgJobKind.Command) continue;
            if (job.Status is not (BorgJobStatus.Running or BorgJobStatus.Pending)) continue;

            var kind = job.JournalEntry?.EventKind;
            if (kind is JournalEventKind.Restore)
                RestoreProgress.SetActiveJob(job);
            else if (kind is JournalEventKind.Backup or JournalEventKind.Prune
                     or JournalEventKind.Check or JournalEventKind.Compact)
                Progress.SetActiveJob(job);
            return;
        }
    }

    // === Stats ===

    [RelayCommand]
    private async Task FetchStats()
    {
        if (SelectedRepository is null || _jobQueue is null) return;

        var repo = SelectedRepository;
        var service = _borgServiceFactory.GetService(repo.BorgVersion);

        repo.IsLoadingStats = true;
        try
        {
            var job = _jobQueue.Enqueue(
                $"{Strings.Get("Statistics")}: {repo.Name}",
                async (j, ct, progress) => await _runner.RunWithPassphraseRetry(
                    repo, () => _runner.RunWithTransientRetry(j,
                        () => service.InfoRepoAsync(repo, ct))),
                BorgJobKind.Query, $"stats:{repo.Path}", repo.Path);
            var result = await job.Completion.Task;

            if (result.Success)
            {
                var stats = ArchiveJsonParser.ParseRepoStats(result.StandardOutput);
                if (stats is var (size, chunks))
                {
                    repo.StatsTotalSize = size;
                    repo.StatsTotalChunks = chunks;
                }
            }
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Failed to fetch repo stats"); }
        finally
        {
            repo.IsLoadingStats = false;
        }
    }

    // === Archive list ===

    [RelayCommand]
    private async Task ListArchives()
    {
        if (SelectedRepository is null || _jobQueue is null) return;

        Archives.Clear();
        var repo = SelectedRepository;
        var service = _borgServiceFactory.GetService(repo.BorgVersion);

        _store.SetLoadingArchives(repo, true);
        try
        {
            var job = _jobQueue.Enqueue(
                $"{Strings.Get("Job.ListArchives")}: {repo.Name}",
                async (j, ct, progress) =>
                {
                    progress.Report(string.Format(Strings.Get("Status.LoadingArchives")));
                    return await _runner.RunWithPassphraseRetry(
                        repo, () => _runner.RunWithTransientRetry(j,
                            () => service.ListArchivesAsync(repo, ct)));
                },
                BorgJobKind.Query, $"list:{repo.Path}", repo.Path);
            var result = await job.Completion.Task;

            if (result.Success)
            {
                var archives = ArchiveJsonParser.ParseArchiveList(result.StandardOutput);
                repo.LoadedArchives = archives;
                if (SelectedRepository == repo)
                    Archives.ReplaceWith(archives);
            }
            else if (SelectedRepository == repo && !result.WasCancelled)
            {
                _logger.LogWarning("Failed to list archives for {Repo}: {Error}", repo.Name, result.ErrorMessage);
            }
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Failed to list archives for {Repo}", repo.Name); }
        finally
        {
            _store.SetLoadingArchives(repo, false);
        }
    }

    // === Sort ===

    private void ToggleSort(SortField field, bool defaultAscending)
    {
        if (_sortField == field)
            _sortAscending = !_sortAscending;
        else
        {
            _sortField = field;
            _sortAscending = defaultAscending;
        }
        ApplySort();
    }

    [RelayCommand]
    private void SortByName() => ToggleSort(SortField.Name, defaultAscending: true);

    [RelayCommand]
    private void SortByDate() => ToggleSort(SortField.Date, defaultAscending: false);

    private void ApplySort()
    {
        var selected = SelectedArchive;
        var sorted = _sortField == SortField.Name
            ? (_sortAscending
                ? Archives.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
                : Archives.OrderByDescending(a => a.Name, StringComparer.OrdinalIgnoreCase))
            : (_sortAscending
                ? Archives.OrderBy(a => a.Date)
                : Archives.OrderByDescending(a => a.Date));

        Archives.ReplaceWith(sorted.ToList());
        SelectedArchive = selected;

        var arrow = _sortAscending ? " ▲" : " ▼";
        NameSortIndicator = _sortField == SortField.Name ? arrow : "";
        DateSortIndicator = _sortField == SortField.Date ? arrow : "";
    }

    // === Archive detail ===

    private void FormatDetail(BorgArchive archive)
    {
        DetailOriginalSize = archive.OriginalSize is { } s ? Strings.FormatBytes(s) : null;
        DetailFileCount = archive.FileCount?.ToString("N0", Strings.Culture);
    }

    private void RefreshArchiveDates()
    {
        var selected = SelectedArchive;
        var items = Archives.ToList();
        Archives.ReplaceWith(items);
        if (selected is not null)
            SelectedArchive = Archives.FirstOrDefault(a => a.Name == selected.Name);
    }

    private void ReformatDetail()
    {
        if (SelectedArchive is { HasDetail: true })
            FormatDetail(SelectedArchive);
    }

    partial void OnSelectedArchiveChanged(BorgArchive? value)
    {
        DetailOriginalSize = null;
        DetailFileCount = null;
        OnPropertyChanged(nameof(HasDetail));

        if (value is null || SelectedRepository is null) return;

        if (value.HasDetail)
        {
            FormatDetail(value);
            OnPropertyChanged(nameof(HasDetail));
        }
        else if (!SelectedRepository.HasError && _settings.AutoLoadArchiveDetails)
        {
            FetchDetailCommand.ExecuteAsync(null);
        }
    }

    [RelayCommand]
    private async Task FetchDetail()
    {
        if (SelectedArchive is null || SelectedRepository is null) return;
        await FetchArchiveInfoAsync(SelectedRepository, SelectedArchive);
    }

    private async Task FetchArchiveInfoAsync(BorgRepository repo, BorgArchive archive)
    {
        if (_jobQueue is null) return;
        var service = _borgServiceFactory.GetService(repo.BorgVersion);
        archive.IsLoadingDetail = true;
        try
        {
            var job = _jobQueue.Enqueue(
                $"Info: {repo.Name}::{archive.Name}",
                async (j, ct, progress) => await _runner.RunWithPassphraseRetry(
                    repo, () => _runner.RunWithTransientRetry(j,
                        () => service.InfoArchiveAsync(repo, archive.Name, ct))),
                BorgJobKind.Query, $"info:{repo.Path}::{archive.Name}", repo.Path);
            var result = await job.Completion.Task;

            if (result.Success)
            {
                var detail = ArchiveJsonParser.ParseArchiveDetail(result.StandardOutput);
                if (detail is var (origSize, fileCount))
                {
                    archive.OriginalSize = origSize;
                    archive.FileCount = fileCount;
                }

                if (SelectedArchive == archive && SelectedRepository == repo && archive.HasDetail)
                {
                    FormatDetail(archive);
                    OnPropertyChanged(nameof(HasDetail));
                }
            }
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Failed to fetch archive info"); }
        finally
        {
            archive.IsLoadingDetail = false;
        }
    }

    // === Restore ===

    [RelayCommand(CanExecute = nameof(CanModifyArchive), AllowConcurrentExecutions = true)]
    private async Task PickAndRestore()
    {
        var path = await _filePicker.PickFolderAsync(Strings.Get("Picker.SelectRestoreDest"));
        if (path is null) return;
        RestorePath = path;
        await Restore();
    }

    [RelayCommand(CanExecute = nameof(CanModifyArchive), AllowConcurrentExecutions = true)]
    private async Task BrowseAndRestore()
    {
        if (SelectedRepository is null || SelectedArchive is null || _jobQueue is null)
            return;

        _jobQueue.CancelQueryByRepoPath(SelectedRepository.Path);

        var previousArchive = Archives
            .Where(a => a.Date < SelectedArchive.Date)
            .OrderByDescending(a => a.Date)
            .FirstOrDefault()?.Name;
        var vm = new BrowseArchiveViewModel(_borgServiceFactory, _jobQueue, _cache, _filePicker, _runner, SelectedRepository, SelectedArchive.Name, previousArchive);
        var window = new Views.BrowseArchiveWindow { DataContext = vm };
        var mainWindow = DialogHelper.GetMainWindow();
        if (mainWindow is null) return;

        await window.ShowDialog(mainWindow);

        if (SelectedArchive is not null && !HasDetail && _settings.AutoLoadArchiveDetails)
            _ = FetchDetailCommand.ExecuteAsync(null);

        if (window.SelectedDestination is null || window.SelectedPaths is null || window.SelectedPaths.Count == 0)
            return;

        if (SelectedRepository is null || SelectedArchive is null)
            return;

        await ExecuteRestore(window.SelectedDestination, window.SelectedPaths, window.SelectedSize);
    }

    [RelayCommand]
    private async Task Restore()
    {
        if (SelectedRepository is null || SelectedArchive is null || string.IsNullOrWhiteSpace(RestorePath))
            return;

        var totalSize = SelectedArchive.OriginalSize ?? 0L;
        await ExecuteRestore(RestorePath, null, totalSize);
    }

    private async Task ExecuteRestore(string restorePath, IReadOnlyList<string>? paths, long totalSize)
    {
        if (SelectedRepository is null || SelectedArchive is null || _jobQueue is null)
            return;

        var repo = SelectedRepository;
        var archiveName = SelectedArchive.Name;
        var service = _borgServiceFactory.GetService(repo.BorgVersion);

        await RunCommandAsync(repo, JournalEventKind.Restore,
            jobName: $"{Strings.Get("Job.Restore")}: {archiveName} → {restorePath}",
            execute: async (j, ct) =>
            {
                j.StatusMessage = string.Format(Strings.Get("Status.Restoring"), archiveName);
                return await _runner.RunWithTransientRetry(j,
                    () => service.ExtractAsync(repo, archiveName, restorePath, paths, ct,
                        onStderrLine: line => BorgProgressParser.Update(j, line)));
            },
            titleArgs: [archiveName],
            onJobCreated: job =>
            {
                job.TotalSize = totalSize;
                _jobQueue.ClearQueryInvalidated();
                RestoreProgress.SetActiveJob(job);
            });

        RestoreProgress.SetActiveJob(null);

        if (SelectedArchive is not null && !HasDetail && _settings.AutoLoadArchiveDetails)
            _ = FetchDetailCommand.ExecuteAsync(null);
    }

    // === Delete archive ===

    [RelayCommand(CanExecute = nameof(CanModifyArchive), AllowConcurrentExecutions = true)]
    private async Task DeleteArchive()
    {
        if (SelectedRepository is null || SelectedArchive is null || _jobQueue is null)
            return;

        // Confirm + passphrase manually so the optimistic Archives.Remove below
        // only fires after the user committed and we have a usable passphrase.
        if (!await DialogHelper.ConfirmAsync(
                string.Format(Strings.Get("ConfirmDeleteArchive"), SelectedArchive.Name)))
            return;

        if (!await _passphrase.EnsurePassphraseAsync(SelectedRepository))
            return;

        var repo = SelectedRepository;
        var archive = SelectedArchive;
        var service = _borgServiceFactory.GetService(repo.BorgVersion);

        Archives.Remove(archive);
        _cache.InvalidateRepo(repo.Path);

        IsDeletingArchive = true;
        try
        {
            await RunCommandAsync(repo, JournalEventKind.Delete,
                jobName: $"{Strings.Get("Job.DeleteArchive")}: {archive.Name}",
                execute: (j, ct) => service.DeleteArchiveAsync(repo, archive.Name, ct),
                titleArgs: [archive.Name]);
        }
        finally
        {
            IsDeletingArchive = false;
        }

        await ListArchives();
    }

    /// <summary>Full command lifecycle: confirm, passphrase, IsBusy, journal, enqueue, complete.</summary>
    private async Task<BorgResult> RunCommandAsync(
        BorgRepository repo,
        JournalEventKind kind,
        string jobName,
        Func<BorgJob, CancellationToken, Task<BorgResult>> execute,
        string? confirmMessage = null,
        object[]? titleArgs = null,
        Action<BorgJob>? onJobCreated = null,
        bool invalidateArchivesOnSuccess = false)
    {
        if (confirmMessage is not null && !await DialogHelper.ConfirmAsync(confirmMessage))
            return new BorgResult(-1, "", "", WasCancelled: true);

        if (!await _passphrase.EnsurePassphraseAsync(repo))
            return new BorgResult(-1, "", "", WasCancelled: true);

        if (repo.IsBusy)
            return new BorgResult(-1, "", "", WasCancelled: true);

        repo.IsBusy = true;
        var journalEntry = _journalService.Add(kind, titleArgs ?? [repo.Name], repo.Name);

        var job = _jobQueue!.Enqueue(
            jobName,
            async (j, ct, _) => await execute(j, ct),
            repoPath: repo.Path,
            journalEntry: journalEntry);

        onJobCreated?.Invoke(job);

        var result = await job.Completion.Task;

        repo.IsBusy = false;
        repo.Passphrase = string.Empty;

        if (result.Success)
        {
            _journalService.Complete(journalEntry, JournalResult.Completed);
            if (invalidateArchivesOnSuccess)
            {
                repo.LoadedArchives = null;
                if (SelectedRepository == repo)
                    InvalidateArchives();
            }
        }
        else if (result.WasCancelled)
        {
            _journalService.Complete(journalEntry, JournalResult.Cancelled);
        }
        else
        {
            _journalService.Complete(journalEntry, JournalResult.Failed, result.ErrorMessage);
        }

        return result;
    }

    private async Task CopySshKeyIfNeeded(BorgRepository repo)
    {
        if (WslHelper.IsRequired && !repo.IsLocal && !string.IsNullOrWhiteSpace(repo.SshKeyPath))
            await _wsl.CopySshKeyAsync(repo.SshKeyPath);
    }
}
