using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BorgMate.Localization;
using BorgMate.Models;
using BorgMate.Services;
using BorgMate.Services.Borg;
using BorgMate.Services.Journal;
using BorgMate.Services.Queue;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace BorgMate.ViewModels;

public partial class ArchiveListViewModel : ViewModelBase
{
    private readonly BorgServiceFactory _borgServiceFactory = null!;
    private readonly IFilePickerService _filePicker = null!;
    private readonly BorgCacheService _cache = null!;
    private readonly IJournalService _journalService = null!;
    private readonly BorgOperationRunner _runner = null!;
    private readonly PassphrasePrompt _passphrase = null!;
    private readonly JobQueueService? _jobQueue;
    private readonly ILogger<ArchiveListViewModel> _logger = null!;

    public ArchiveListViewModel() { }

    public ArchiveListViewModel(BorgServiceFactory borgServiceFactory, IFilePickerService filePicker, BorgCacheService cache, IJournalService journalService, BorgOperationRunner runner, PassphrasePrompt passphrase, JobQueueService jobQueue, ILogger<ArchiveListViewModel> logger)
    {
        _borgServiceFactory = borgServiceFactory;
        _filePicker = filePicker;
        _cache = cache;
        _journalService = journalService;
        _runner = runner;
        _passphrase = passphrase;
        _jobQueue = jobQueue;
        _logger = logger;
        Strings.LanguageChanged += ReformatDetail;
        Strings.LanguageChanged += RefreshArchiveDates;
    }

    [ObservableProperty]
    private BorgRepository? _repository;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedArchive))]
    [NotifyPropertyChangedFor(nameof(CanModifyArchive))]
    private BorgArchive? _selectedArchive;

    public bool HasSelectedArchive => SelectedArchive is not null;
    public bool CanModifyArchive => SelectedArchive is not null && Repository is { IsBusy: false };
    public bool IsRepoIdle => Repository is null or { IsBusy: false };

    [ObservableProperty]
    private bool _isActive;

    // Archive detail panel
    [ObservableProperty]
    private bool _isLoadingDetail;

    [ObservableProperty]
    private string? _detailOriginalSize;

    [ObservableProperty]
    private string? _detailFileCount;

    public bool HasDetail => DetailOriginalSize is not null;

    private void FormatDetail(BorgCacheService.ArchiveDetail detail)
    {
        DetailOriginalSize = Strings.FormatBytes(detail.OriginalSize);
        DetailFileCount = detail.FileCount.ToString("N0", Strings.Culture);
    }

    /// <summary>
    /// Replaces the archive collection in-place to force Avalonia compiled bindings
    /// to re-evaluate DateDisplay after a language change.
    /// </summary>
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
        if (SelectedArchive is null || Repository is null) return;
        var cached = _cache.GetArchiveDetail(Repository.Path, SelectedArchive.Name);
        if (cached is not null)
            FormatDetail(cached);
    }

    partial void OnSelectedArchiveChanged(BorgArchive? value)
    {
        DetailOriginalSize = null;
        DetailFileCount = null;
        OnPropertyChanged(nameof(HasDetail));

        if (value is null || Repository is null) return;

        var cached = _cache.GetArchiveDetail(Repository.Path, value.Name);
        if (cached is not null)
        {
            FormatDetail(cached);
            OnPropertyChanged(nameof(HasDetail));
        }
        else
        {
            FetchDetailCommand.ExecuteAsync(null);
        }
    }

    [RelayCommand]
    private async Task FetchDetail()
    {
        if (SelectedArchive is null || Repository is null) return;
        await FetchArchiveInfoAsync(Repository, SelectedArchive.Name);
    }

    private async Task FetchArchiveInfoAsync(BorgRepository repo, string archiveName)
    {
        if (_jobQueue is null) return;
        var service = _borgServiceFactory.GetService(repo.BorgVersion);
        IsLoadingDetail = true;
        try
        {
            var job = _jobQueue.Enqueue(
                $"Info: {repo.Name}::{archiveName}",
                async (j, ct, progress) => await _runner.RunWithPassphraseRetry(
                    repo, () => _runner.RunWithTransientRetry(j,
                        () => service.InfoArchiveAsync(repo, archiveName, ct))),
                BorgJobKind.Query, $"info:{repo.Path}::{archiveName}", repo.Path);
            var result = await job.Completion.Task;

            if (result.Success)
            {
                var detail = ArchiveJsonParser.ParseArchiveDetail(result.StandardOutput);
                if (detail is not null)
                    _cache.SetArchiveDetail(repo.Path, archiveName, detail);

                // Update UI only if selection hasn't changed
                if (SelectedArchive?.Name == archiveName && Repository == repo && detail is not null)
                {
                    FormatDetail(detail);
                    OnPropertyChanged(nameof(HasDetail));
                }
            }
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Failed to fetch archive info"); }
        finally
        {
            IsLoadingDetail = false;
        }
    }

    partial void OnRepositoryChanged(BorgRepository? oldValue, BorgRepository? newValue)
    {
        if (oldValue is not null)
            oldValue.PropertyChanged -= OnRepoPropertyChanged;
        if (newValue is not null)
            newValue.PropertyChanged += OnRepoPropertyChanged;

        // Save current archives to cache
        if (oldValue is not null)
            _cache.SetArchiveList(oldValue.Path, Archives.ToList());

        // Restore from cache or clear
        Archives.Clear();
        SelectedArchive = null;
        var cachedList = newValue is not null ? _cache.GetArchiveList(newValue.Path) : null;
        if (cachedList is not null)
        {
            Archives.ReplaceWith(cachedList);
        }
        else if (newValue is not null && IsActive && !newValue.IsBusy)
        {
            ListArchivesCommand.ExecuteAsync(null);
        }
    }

    private void OnRepoPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BorgRepository.IsBusy))
        {
            OnPropertyChanged(nameof(CanModifyArchive));
            OnPropertyChanged(nameof(IsRepoIdle));
        }
    }

    partial void OnIsActiveChanged(bool value)
    {
        if (value && Repository is not null && Archives.Count == 0 && !Repository.IsBusy)
            ListArchivesCommand.ExecuteAsync(null);
    }

    public void InvalidateArchives()
    {
        if (Repository is not null)
            _cache.InvalidateRepo(Repository.Path);
        Archives.Clear();
        SelectedArchive = null;
        if (IsActive && Repository is not null && !Repository.IsBusy)
            ListArchivesCommand.ExecuteAsync(null);
    }

    /// <summary>
    /// Fetches archives only if the list is empty (e.g. a cancelled query left no data).
    /// </summary>
    public void FetchArchivesIfEmpty()
    {
        if (Archives.Count == 0 && IsActive && Repository is not null && !Repository.IsBusy)
            ListArchivesCommand.ExecuteAsync(null);
    }

    [ObservableProperty]
    private string _restorePath = string.Empty;

    private enum SortField { Date, Name }
    private SortField _sortField = SortField.Date;
    private bool _sortAscending;

    [ObservableProperty]
    private string _nameSortIndicator = "";

    [ObservableProperty]
    private string _dateSortIndicator = " ▼";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsArchiveBusy))]
    private bool _isLoadingArchives;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsArchiveBusy))]
    private bool _isDeletingArchive;

    public bool IsArchiveBusy => IsLoadingArchives || IsDeletingArchive;

    public ObservableCollection<BorgArchive> Archives { get; } = [];

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

    [RelayCommand]
    private async Task ListArchives()
    {
        if (Repository is null || _jobQueue is null) return;

        Archives.Clear();
        var repo = Repository;
        var service = _borgServiceFactory.GetService(repo.BorgVersion);

        IsLoadingArchives = true;
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

            // Parse and cache results for the repo that initiated the request
            if (result.Success)
            {
                var archives = ArchiveJsonParser.ParseArchiveList(result.StandardOutput);
                _cache.SetArchiveList(repo.Path, archives);

                // Display only if this repo is still selected
                if (Repository == repo)
                    Archives.ReplaceWith(archives);
            }
            else if (Repository == repo && !result.WasCancelled)
            {
                _logger.LogWarning("Failed to list archives for {Repo}: {Error}", repo.Name, result.ErrorMessage);
            }
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Failed to list archives for {Repo}", repo.Name); }
        finally
        {
            IsLoadingArchives = false;
        }
    }

    [RelayCommand]
    private async Task PickAndRestore()
    {
        var path = await _filePicker.PickFolderAsync(Strings.Get("Picker.SelectRestoreDest"));
        if (path is null) return;
        RestorePath = path;
        await Restore();
    }

    [RelayCommand]
    private async Task BrowseAndRestore()
    {
        if (Repository is null || SelectedArchive is null || _jobQueue is null)
            return;

        _jobQueue.CancelQueryByRepoPath(Repository.Path);

        var previousArchive = Archives
            .Where(a => a.Date < SelectedArchive.Date)
            .OrderByDescending(a => a.Date)
            .FirstOrDefault()?.Name;
        var vm = new BrowseArchiveViewModel(_borgServiceFactory, _jobQueue, _cache, _filePicker, _runner, Repository, SelectedArchive.Name, previousArchive);
        var window = new Views.BrowseArchiveWindow { DataContext = vm };
        var mainWindow = DialogHelper.GetMainWindow();
        if (mainWindow is null) return;

        await window.ShowDialog(mainWindow);

        // Re-fetch archive detail if it was cancelled
        if (SelectedArchive is not null && !HasDetail)
            _ = FetchDetailCommand.ExecuteAsync(null);

        if (window.SelectedDestination is null || window.SelectedPaths is null || window.SelectedPaths.Count == 0)
            return;

        if (Repository is null || SelectedArchive is null)
            return;

        await ExecuteRestore(window.SelectedDestination, window.SelectedPaths, window.SelectedSize);
    }

    [RelayCommand]
    private async Task Restore()
    {
        if (Repository is null || SelectedArchive is null || string.IsNullOrWhiteSpace(RestorePath))
            return;

        var totalSize = _cache.GetArchiveDetail(Repository.Path, SelectedArchive.Name)?.OriginalSize ?? 0L;
        await ExecuteRestore(RestorePath, null, totalSize);
    }

    private async Task ExecuteRestore(string restorePath, IReadOnlyList<string>? paths, long totalSize)
    {
        if (Repository is null || SelectedArchive is null || _jobQueue is null)
            return;

        if (!await _passphrase.EnsurePassphraseAsync(Repository))
            return;

        var repo = Repository;
        var archiveName = SelectedArchive.Name;
        var service = _borgServiceFactory.GetService(repo.BorgVersion);

        var result = await EnqueueWithJournal(repo, JournalEventKind.Restore, [archiveName],
            $"{Strings.Get("Job.Restore")}: {archiveName} → {restorePath}",
            async (j, ct, progress) =>
            {
                progress.Report(string.Format(Strings.Get("Status.Restoring"), archiveName));
                return await _runner.RunWithTransientRetry(j,
                    () => service.ExtractAsync(repo, archiveName, restorePath, paths, ct,
                        onStderrLine: line => BorgProgressParser.Update(j, line)));
            },
            job => { job.TotalSize = totalSize; _jobQueue.ClearQueryInvalidated(); SetActiveJob(job); });
        SetActiveJob(null);

        if (SelectedArchive is not null && !HasDetail)
            _ = FetchDetailCommand.ExecuteAsync(null);
    }

    // --- Active Job Progress ---

    public JobProgressTracker Progress { get; } = new(confirmCancel: true);

    private void SetActiveJob(BorgJob? job) => Progress.SetActiveJob(job);

    /// <summary>
    /// Enqueues a command job with standard lifecycle: sets repo.IsBusy, creates a journal entry,
    /// enqueues the job, awaits completion, clears IsBusy, and records the result in the journal.
    /// </summary>
    private async Task<BorgResult> EnqueueWithJournal(
        BorgRepository repo,
        JournalEventKind eventKind,
        object[] titleArgs,
        string jobName,
        Func<BorgJob, CancellationToken, IProgress<string>, Task<BorgResult>> work,
        Action<BorgJob>? configureJob = null)
    {
        repo.IsBusy = true;
        var journalEntry = _journalService.Add(eventKind, titleArgs, repo.Name);
        var job = _jobQueue!.Enqueue(jobName, work, repoPath: repo.Path, journalEntry: journalEntry);
        configureJob?.Invoke(job);
        var result = await job.Completion.Task;
        repo.IsBusy = false;
        repo.Passphrase = string.Empty;

        if (result.Success)
            _journalService.Complete(journalEntry, JournalResult.Completed);
        else if (result.WasCancelled)
            _journalService.Complete(journalEntry, JournalResult.Cancelled);
        else
            _journalService.Complete(journalEntry, JournalResult.Failed, result.ErrorMessage);

        return result;
    }

    [RelayCommand]
    private async Task DeleteArchive()
    {
        if (Repository is null || SelectedArchive is null || _jobQueue is null)
            return;

        if (!await DialogHelper.ConfirmAsync(
                string.Format(Strings.Get("ConfirmDeleteArchive"), SelectedArchive.Name)))
            return;

        if (!await _passphrase.EnsurePassphraseAsync(Repository))
            return;

        var repo = Repository;
        var archive = SelectedArchive;
        var service = _borgServiceFactory.GetService(repo.BorgVersion);

        Archives.Remove(archive);
        _cache.InvalidateRepo(repo.Path);

        IsDeletingArchive = true;
        await EnqueueWithJournal(repo, JournalEventKind.Delete, [archive.Name],
            $"{Strings.Get("Job.DeleteArchive")}: {archive.Name}",
            async (j, ct, progress) => await service.DeleteArchiveAsync(repo, archive.Name, ct));
        IsDeletingArchive = false;

        await ListArchives();
    }

}
