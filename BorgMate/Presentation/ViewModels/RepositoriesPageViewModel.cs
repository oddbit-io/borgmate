using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Threading;
using BorgMate.Domain.Archives;
using BorgMate.Domain.Journal;
using BorgMate.Domain.Settings;
using BorgMate.Persistence.Configuration;
using BorgMate.Presentation.Dialogs;
using BorgMate.Presentation.Views;
using BorgMate.Runtime.Jobs;
using BorgMate.Runtime.Operations;
using BorgMate.Runtime.Orchestration;
using BorgMate.Runtime.Progress;
using BorgMate.Runtime.Security;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using OneOf;
using RepositoryPresentationState = BorgMate.Presentation.State.RepositoryPresentationState;

namespace BorgMate.Presentation.ViewModels;

/// <summary>Single VM for the entire Repositories page (left list + right archives).</summary>
public partial class RepositoriesPageViewModel : ViewModelBase
{
    private readonly IArchiveOperationRunner _archiveOperations = null!;
    private readonly IFilePickerService _filePicker = null!;
    private readonly IJobController? _jobController;
    private readonly ILogger<RepositoriesPageViewModel> _logger = null!;
    private readonly IPassphraseProvider _passphraseProvider = null!;
    private readonly IRepositoryConfigStore _repositoryConfigStore = null!;
    private readonly IRepositoryOperationRunner _repositoryOperations = null!;
    private readonly AppSettings _settings = null!;
    private readonly ISettingsStore _settingsStore = null!;
    private readonly RepositoryPresentationState _store = null!;
    private readonly IRepositoryVerificationOperationRunner _verificationOperations = null!;

    [ObservableProperty]
    private string _dateSortIndicator = " ▼";

    [ObservableProperty]
    private string? _detailFileCount;

    // === Archive detail state ===

    [ObservableProperty]
    private string? _detailOriginalSize;

    // === Page activation (set from MainWindowViewModel based on ActivePage) ===

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsArchiveBusy))]
    [NotifyPropertyChangedFor(nameof(ShowArchiveListPlaceholder))]
    private bool _isDeletingArchive;

    // === Loading flags ===

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsArchiveBusy))]
    [NotifyPropertyChangedFor(nameof(ShowArchiveListPlaceholder))]
    private bool _isLoadingArchives;

    [ObservableProperty]
    private string _nameSortIndicator = "";

    private RepositoryViewModel? _previousSelection;

    // === Restore path ===

    [ObservableProperty]
    private string _restorePath = string.Empty;

    // === Selected archive (presentation state) ===

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedArchive))]
    [NotifyPropertyChangedFor(nameof(CanModifyArchive))]
    [NotifyCanExecuteChangedFor(nameof(DeleteArchiveCommand))]
    [NotifyCanExecuteChangedFor(nameof(PickAndRestoreCommand))]
    [NotifyCanExecuteChangedFor(nameof(BrowseAndRestoreCommand))]
    private BorgArchive? _selectedArchive;

    private bool _sortAscending;
    private SortField _sortField = SortField.Date;

    // === Constructors ===

    public RepositoriesPageViewModel()
    {
        _store = new RepositoryPresentationState();
    }

    public RepositoriesPageViewModel(
        AppSettings settings,
        ISettingsStore settingsStore,
        IRepositoryConfigStore repositoryConfigStore,
        IFilePickerService filePicker,
        IRepositoryOperationRunner repositoryOperations,
        IArchiveOperationRunner archiveOperations,
        IRepositoryVerificationOperationRunner verificationOperations,
        IPassphraseProvider passphraseProvider,
        IJobController jobController,
        RepositoryPresentationState store,
        ILogger<RepositoriesPageViewModel> logger)
    {
        _settings = settings;
        _settingsStore = settingsStore;
        _repositoryConfigStore = repositoryConfigStore;
        _filePicker = filePicker;
        _repositoryOperations = repositoryOperations;
        _archiveOperations = archiveOperations;
        _verificationOperations = verificationOperations;
        _passphraseProvider = passphraseProvider;
        _jobController = jobController;
        _store = store;
        _logger = logger;

        _store.PropertyChanged += OnStorePropertyChanged;
        _jobController.JobCompleted += OnJobCompleted;
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

        _settingsStore.Changed += _ => OnSettingsMaybeChanged();
    }

    // === Selection (forwarded to store) ===

    public ObservableCollection<RepositoryViewModel> Repositories => _store.Repositories;

    public RepositoryViewModel? SelectedRepository
    {
        get => _store.SelectedRepository;
        set => _store.SelectedRepository = value;
    }

    public bool HasSelectedArchive => SelectedArchive is not null;

    // === Computed selection state ===

    public bool CanEditOrRemove => SelectedRepository is not null && !SelectedRepository.IsBusy;

    public bool CanRunBackup => SelectedRepository is not null && !SelectedRepository.IsBusy &&
                                SelectedRepository.SourceDirectories.Count > 0;

    public bool CanModifyArchive =>
        SelectedArchive is not null && SelectedRepository is { IsBusy: false, HasError: false };

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
    ///     Visible when AutoLoadArchives is off, the list is empty, and we are
    ///     not currently fetching — prompts the user to click Refresh.
    /// </summary>
    public bool ShowArchiveListPlaceholder =>
        !AutoLoadArchives &&
        SelectedRepository is { IsBusy: false, HasError: false } &&
        !IsArchiveBusy &&
        Archives.Count == 0;

    public bool IsArchiveBusy => IsLoadingArchives || IsDeletingArchive;

    public bool HasDetail => DetailOriginalSize is not null;

    // === Collections ===

    public ObservableCollection<BorgArchive> Archives { get; } = [];

    // === Trackers ===

    public JobProgressTracker Progress { get; } = new(true);
    public JobProgressTracker RestoreProgress { get; } = new(true);

    /// <summary>
    ///     Re-broadcasts the AutoLoad* getters whenever any save request fires. Cheap and
    ///     covers settings-dialog edits without an additional event channel — view bindings
    ///     to <see cref="AutoLoadArchives" />, <see cref="AutoLoadStats" />, etc. refresh.
    /// </summary>
    private void OnSettingsMaybeChanged()
    {
        OnPropertyChanged(nameof(AutoLoadArchives));
        OnPropertyChanged(nameof(AutoLoadStats));
        OnPropertyChanged(nameof(AutoLoadArchiveDetails));
        OnPropertyChanged(nameof(ShowArchiveListPlaceholder));
    }

    private void RequestSave()
    {
        _settingsStore.RequestSave();
    }

    private void OnJobCompleted(JobCompleted completion)
    {
        if (Application.Current is null || Dispatcher.UIThread.CheckAccess())
            HandleJobCompleted(completion);
        else
            Dispatcher.UIThread.Post(() => HandleJobCompleted(completion));
    }

    internal void HandleJobCompleted(JobCompleted completion)
    {
        var job = completion.Job;
        var result = completion.Result;
        if (job.RepoPath is null) return;
        if (result.WasCancelled) return;

        var repo = _store.FindByPath(job.RepoPath);
        if (repo is null) return;

        if (result.Success)
        {
            repo.HasError = false;
            repo.LastError = null;
            return;
        }

        repo.HasError = true;
        repo.LastError = result.ErrorMessage;
    }

    // === Selection / store handlers ===

    private void OnStorePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SelectedRepository)) return;

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
            Archives.ReplaceWith(archives);
        else if (IsActive && !newRepo.IsBusy && !newRepo.HasError && _settings.AutoLoadArchives)
            ListArchivesCommand.ExecuteAsync(null);

        if (!newRepo.HasError && !newRepo.HasStats && _settings.AutoLoadStats)
            FetchStatsCommand.ExecuteAsync(null);

        OnPropertyChanged(nameof(ShowArchiveListPlaceholder));

        UpdateActiveJob();
    }

    private void OnSelectedRepoPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(RepositoryViewModel.IsBusy) or nameof(RepositoryViewModel.HasError))
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
            if (e.PropertyName == nameof(RepositoryViewModel.IsBusy))
                UpdateActiveJob();
        }

        if (e.PropertyName == nameof(RepositoryViewModel.LastError))
            OnPropertyChanged(nameof(SelectedRepoLastError));

        // Recovery: when HasError clears, refetch the archive list if empty.
        if (e.PropertyName == nameof(RepositoryViewModel.HasError) &&
            _store.SelectedRepository is { HasError: false } &&
            Archives.Count == 0)
            Dispatcher.UIThread.Post(FetchArchivesIfEmpty);
    }

    partial void OnIsActiveChanged(bool value)
    {
        if (value && _settings.AutoLoadArchives && _store.SelectedRepository is { IsBusy: false, HasError: false } &&
            Archives.Count == 0)
            ListArchivesCommand.ExecuteAsync(null);
    }

    public void InvalidateArchives()
    {
        if (_store.SelectedRepository is { } repo)
        {
            repo.LoadedArchives = null;
            _archiveOperations.InvalidateRepositoryCache(repo);
        }

        Archives.Clear();
        SelectedArchive = null;
        if (IsActive && _settings.AutoLoadArchives && _store.SelectedRepository is { IsBusy: false, HasError: false })
            ListArchivesCommand.ExecuteAsync(null);
        OnPropertyChanged(nameof(ShowArchiveListPlaceholder));
    }

    public void FetchArchivesIfEmpty()
    {
        if (Archives.Count == 0 && IsActive && _settings.AutoLoadArchives && _store.SelectedRepository is
                { IsBusy: false, HasError: false })
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
            _filePicker, _verificationOperations, _settings.ExperimentalBorg2Support);
        await ShowEditorAsync(vm);

        if (vm.IsSaved && vm.Repository is { } repo)
        {
            _store.Add(repo);
            await _repositoryConfigStore.AddAsync(repo);
            SelectedRepository = repo;
            RequestSave();
        }
    }

    [RelayCommand]
    private async Task OpenExisting()
    {
        var vm = RepositoryEditorViewModel.ForOpen(
            _filePicker, _verificationOperations, _settings.ExperimentalBorg2Support);
        await ShowEditorAsync(vm);

        if (vm.IsSaved && vm.Repository is { } repo)
        {
            _store.Add(repo);
            await _repositoryConfigStore.AddAsync(repo);
            SelectedRepository = repo;
            RequestSave();
        }
    }

    [RelayCommand]
    private async Task EditRepository(RepositoryViewModel repo)
    {
        _jobController?.CancelQueries(repo.Path);
        var persistedBeforeEdit = JsonAppConfigStore.Clone(repo.Model);

        var vm = RepositoryEditorViewModel.ForEdit(
            _filePicker, repo, _verificationOperations, _settings.ExperimentalBorg2Support);
        await ShowEditorAsync(vm);

        if (vm.IsSaved)
        {
            await _repositoryConfigStore.RemoveAsync(persistedBeforeEdit);
            await _repositoryConfigStore.AddAsync(repo);
            RequestSave();
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
    private async Task DuplicateRepository(RepositoryViewModel repo)
    {
        if (repo is null) return;

        var copyName = GenerateUniqueCopyName(repo.Name);
        var vm = RepositoryEditorViewModel.ForDuplicate(
            _filePicker, repo, copyName, _verificationOperations, _settings.ExperimentalBorg2Support);
        await ShowEditorAsync(vm);

        if (vm.IsSaved && vm.Repository is { } newRepo)
        {
            _store.Add(newRepo);
            await _repositoryConfigStore.AddAsync(newRepo);
            SelectedRepository = newRepo;
            RequestSave();
        }
    }

    internal string GenerateUniqueCopyName(string baseName)
    {
        return _repositoryConfigStore.GenerateUniqueCopyName(baseName);
    }

    [RelayCommand]
    private async Task RemoveRepository(RepositoryViewModel repo)
    {
        if (!await DialogHelper.ConfirmAsync(string.Format(Strings.Get("ConfirmDeleteRepo"), repo.Name)))
            return;

        _store.Remove(repo);
        await _repositoryConfigStore.RemoveAsync(repo);
        RequestSave();
    }

    // === Backup ===

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task RunBackup()
    {
        if (SelectedRepository is { } repo && repo.SourceDirectories.Count > 0)
            await BackupRepositoryAsync(repo);
    }

    private async Task<OperationError?> RunRepositoryOperationAsync<T>(
        RepositoryViewModel repo,
        Func<IOperationProgressSink, Task<OneOf<T, OperationError>>> operation,
        JobProgressTracker? tracker = null)
    {
        if (repo.IsBusy)
            return new OperationError.Cancelled();

        repo.IsBusy = true;

        try
        {
            var operationTask = operation(NullOperationProgressSink.Instance);
            UpdateActiveJob();
            var result = await operationTask;
            return result.IsT0 ? null : result.AsT1;
        }
        finally
        {
            repo.IsBusy = false;
            tracker?.SetActiveJob(null);
            UpdateActiveJob();
        }
    }

    private async Task BackupRepositoryAsync(RepositoryViewModel repo)
    {
        if (repo.SourceDirectories.Count == 0) return;

        var error = await RunRepositoryOperationAsync(
            repo,
            sink => _repositoryOperations.BackupAsync(new BackupOperationOptions(repo, sink)),
            Progress);

        if (error is null)
        {
            repo.RefreshScheduleDisplay();
            InvalidateArchives();
            if (SelectedRepository == repo)
                _ = FetchStatsCommand.ExecuteAsync(null);

            if (repo.Schedule.RunPruneAfterBackup && repo.PruneOptions.HasAnyRetention)
                await PruneRepositoryAsync(repo);
        }
    }

    // === Maintenance (prune / check / compact) ===

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task PruneRepo()
    {
        if (SelectedRepository is { } repo)
            await PruneRepositoryAsync(repo, true);
    }

    private async Task PruneRepositoryAsync(RepositoryViewModel repo, bool confirm = false)
    {
        if (confirm && !await DialogHelper.ConfirmAsync(string.Format(Strings.Get("ConfirmPrune"), repo.Name)))
            return;

        var shouldCompact = false;
        var error = await RunRepositoryOperationAsync(
            repo,
            async sink =>
            {
                var result = await _repositoryOperations.PruneAsync(
                    new PruneOperationOptions(repo, sink, repo.PruneOptions.CompactAfterPrune));
                if (result.IsT0)
                    shouldCompact = result.AsT0.ShouldCompact;
                return result;
            },
            Progress);

        if (error is null)
        {
            InvalidateArchives();
            if (shouldCompact)
                await CompactRepositoryAsync(repo);
        }
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task CheckRepo()
    {
        if (SelectedRepository is not { } repo) return;

        await RunRepositoryOperationAsync(
            repo,
            sink => _repositoryOperations.CheckAsync(new CheckOperationOptions(repo, sink)),
            Progress);
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task CompactRepo()
    {
        if (SelectedRepository is { } repo)
            await CompactRepositoryAsync(repo, true);
    }

    private async Task CompactRepositoryAsync(RepositoryViewModel repo, bool confirm = false)
    {
        if (confirm && !await DialogHelper.ConfirmAsync(string.Format(Strings.Get("ConfirmCompact"), repo.Name)))
            return;

        var error = await RunRepositoryOperationAsync(
            repo,
            sink => _repositoryOperations.CompactAsync(new CompactOperationOptions(repo, sink)),
            Progress);

        if (error is null)
        {
            InvalidateArchives();
            _ = FetchStatsCommand.ExecuteAsync(null);
        }
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task ChangePassphrase()
    {
        if (SelectedRepository is not { } repo) return;
        if (!repo.IsEncrypted) return;

        // Unlock the repo first so the new-passphrase dialog comes after the old-passphrase prompt.
        var ensure = await _passphraseProvider.EnsureAsync(repo);
        if (ensure.IsT1) return;

        var parent = DialogHelper.GetMainWindow();
        if (parent is null) return;

        var (newPass, saveToKeychain) = await DialogHelper.ShowNewPassphraseDialogAsync(
            parent,
            Strings.Get("ChangePassphrase"),
            string.Format(Strings.Get("ChangePassphrase.Prompt"), repo.Name));
        if (newPass is null) return;

        await RunRepositoryOperationAsync(
            repo,
            sink => _repositoryOperations.ChangePassphraseAsync(
                new ChangePassphraseOperationOptions(repo, sink, newPass, saveToKeychain)),
            Progress);
    }

    // === Active job tracking for the detail panel ===

    /// <summary>
    ///     Re-binds Progress / RestoreProgress to whatever job is in flight for the
    ///     selected repo. Called on selection change so the detail panel doesn't go
    ///     blank when re-selecting a busy repo.
    /// </summary>
    private void UpdateActiveJob()
    {
        Progress.SetActiveJob(null);
        RestoreProgress.SetActiveJob(null);

        if (_store.SelectedRepository is not { IsBusy: true } repo || _jobController is null)
            return;

        foreach (var job in _jobController.GetSnapshot().Jobs)
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
        if (SelectedRepository is null) return;

        var repo = SelectedRepository;

        repo.IsLoadingStats = true;
        try
        {
            var result = await _archiveOperations.GetRepositoryStatsAsync(
                new RepositoryStatsOperationOptions(repo, NullOperationProgressSink.Instance));
            if (result.IsT0)
            {
                repo.StatsTotalSize = result.AsT0.TotalSize;
                repo.StatsTotalChunks = result.AsT0.TotalChunks;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch repo stats");
        }
        finally
        {
            repo.IsLoadingStats = false;
        }
    }

    // === Archive list ===

    [RelayCommand]
    private async Task ListArchives()
    {
        if (SelectedRepository is null) return;

        Archives.Clear();
        var repo = SelectedRepository;

        _store.SetLoadingArchives(repo, true);
        try
        {
            var result = await _archiveOperations.ListArchivesAsync(
                new ArchiveListOperationOptions(repo, NullOperationProgressSink.Instance));

            if (result.IsT0)
            {
                var archives = result.AsT0.Archives.ToList();
                repo.LoadedArchives = archives;
                if (SelectedRepository == repo)
                    Archives.ReplaceWith(archives);
            }
            else if (SelectedRepository == repo && result.AsT1 is not OperationError.Cancelled)
            {
                _logger.LogWarning("Failed to list archives for {Repo}: {Error}", repo.Name,
                    result.AsT1.ToDisplayMessage());
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to list archives for {Repo}", repo.Name);
        }
        finally
        {
            _store.SetLoadingArchives(repo, false);
        }
    }

    // === Sort ===

    private void ToggleSort(SortField field, bool defaultAscending)
    {
        if (_sortField == field)
        {
            _sortAscending = !_sortAscending;
        }
        else
        {
            _sortField = field;
            _sortAscending = defaultAscending;
        }

        ApplySort();
    }

    [RelayCommand]
    private void SortByName()
    {
        ToggleSort(SortField.Name, true);
    }

    [RelayCommand]
    private void SortByDate()
    {
        ToggleSort(SortField.Date, false);
    }

    private void ApplySort()
    {
        var selected = SelectedArchive;
        var sorted = _sortField == SortField.Name
            ? _sortAscending
                ? Archives.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
                : Archives.OrderByDescending(a => a.Name, StringComparer.OrdinalIgnoreCase)
            : _sortAscending
                ? Archives.OrderBy(a => a.Date)
                : Archives.OrderByDescending(a => a.Date);

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
            SelectedArchive = Archives.FirstOrDefault(a => a is not null && a.Name == selected.Name);
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

    private async Task FetchArchiveInfoAsync(RepositoryViewModel repo, BorgArchive archive)
    {
        archive.IsLoadingDetail = true;
        try
        {
            var result = await _archiveOperations.GetArchiveDetailAsync(
                new ArchiveDetailOperationOptions(repo, NullOperationProgressSink.Instance, archive.Name));

            if (result.IsT0)
            {
                archive.OriginalSize = result.AsT0.OriginalSize;
                archive.FileCount = result.AsT0.FileCount;

                if (SelectedArchive == archive && SelectedRepository == repo && archive.HasDetail)
                {
                    FormatDetail(archive);
                    OnPropertyChanged(nameof(HasDetail));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch archive info");
        }
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

        var extractOptions = await PromptRestoreOptionsAsync();
        if (extractOptions is null) return;

        RestorePath = path;
        if (SelectedArchive is null) return;

        var totalSize = SelectedArchive.OriginalSize ?? 0L;
        await ExecuteRestore(path, null, totalSize, extractOptions);
    }

    [RelayCommand(CanExecute = nameof(CanModifyArchive), AllowConcurrentExecutions = true)]
    private async Task BrowseAndRestore()
    {
        if (SelectedRepository is null || SelectedArchive is null || _jobController is null)
            return;

        _jobController.CancelQueries(SelectedRepository.Path);

        var previousArchive = Archives
            .Where(a => a.Date < SelectedArchive.Date)
            .OrderByDescending(a => a.Date)
            .FirstOrDefault()?.Name;
        var vm = new BrowseArchiveViewModel(_archiveOperations, _filePicker, SelectedRepository, SelectedArchive.Name,
            previousArchive);
        var window = new BrowseArchiveWindow { DataContext = vm };
        var mainWindow = DialogHelper.GetMainWindow();
        if (mainWindow is null) return;

        await window.ShowDialog(mainWindow);

        if (SelectedArchive is not null && !HasDetail && _settings.AutoLoadArchiveDetails)
            _ = FetchDetailCommand.ExecuteAsync(null);

        if (window.SelectedDestination is null || window.SelectedPaths is null || window.SelectedPaths.Count == 0)
            return;

        if (SelectedRepository is null || SelectedArchive is null)
            return;

        var extractOptions = await PromptRestoreOptionsAsync();
        if (extractOptions is null) return;

        await ExecuteRestore(window.SelectedDestination, window.SelectedPaths, window.SelectedSize, extractOptions);
    }

    [RelayCommand]
    private async Task Restore()
    {
        if (SelectedRepository is null || SelectedArchive is null || string.IsNullOrWhiteSpace(RestorePath))
            return;

        var extractOptions = await PromptRestoreOptionsAsync();
        if (extractOptions is null) return;

        var totalSize = SelectedArchive.OriginalSize ?? 0L;
        await ExecuteRestore(RestorePath, null, totalSize, extractOptions);
    }

    private async Task<BorgExtractOptions?> PromptRestoreOptionsAsync()
    {
        var mainWindow = DialogHelper.GetMainWindow();
        if (mainWindow is null) return null;

        _settings.DefaultExtractOptions ??= new BorgExtractOptions();
        var window = new RestoreOptionsWindow(_settings.DefaultExtractOptions.Clone());
        await window.ShowDialog(mainWindow);

        if (window.ExtractOptions is null)
            return null;

        var options = window.ExtractOptions.Clone();
        if (window.SaveAsDefault)
        {
            _settings.DefaultExtractOptions = options.Clone();
            _settingsStore.RequestSave();
        }

        return options;
    }

    private async Task ExecuteRestore(
        string restorePath,
        IReadOnlyList<string>? paths,
        long totalSize,
        BorgExtractOptions extractOptions)
    {
        if (SelectedRepository is null || SelectedArchive is null || _jobController is null)
            return;

        var repo = SelectedRepository;
        var archiveName = SelectedArchive.Name;

        _jobController.ClearQueryInvalidated();
        await RunRepositoryOperationAsync(
            repo,
            sink => _repositoryOperations.RestoreAsync(
                new RestoreOperationOptions(repo, sink, archiveName, restorePath, extractOptions, paths, totalSize)),
            RestoreProgress);

        if (SelectedArchive is not null && !HasDetail && _settings.AutoLoadArchiveDetails)
            _ = FetchDetailCommand.ExecuteAsync(null);
    }

    // === Delete archive ===

    [RelayCommand(CanExecute = nameof(CanModifyArchive), AllowConcurrentExecutions = true)]
    private async Task DeleteArchive()
    {
        if (SelectedRepository is null || SelectedArchive is null)
            return;

        // Confirm + passphrase manually so the optimistic Archives.Remove below
        // only fires after the user committed and we have a usable passphrase.
        if (!await DialogHelper.ConfirmAsync(
                string.Format(Strings.Get("ConfirmDeleteArchive"), SelectedArchive.Name)))
            return;

        var ensure = await _passphraseProvider.EnsureAsync(SelectedRepository);
        if (ensure.IsT1)
            return;

        var repo = SelectedRepository;
        var archive = SelectedArchive;

        Archives.Remove(archive);
        _archiveOperations.InvalidateRepositoryCache(repo);

        IsDeletingArchive = true;
        try
        {
            await RunRepositoryOperationAsync(
                repo,
                sink => _repositoryOperations.DeleteArchiveAsync(
                    new DeleteArchiveOperationOptions(repo, sink, archive.Name)));
        }
        finally
        {
            IsDeletingArchive = false;
        }

        await ListArchives();
    }

    // === Sort state ===

    private enum SortField
    {
        Date,
        Name
    }
}
