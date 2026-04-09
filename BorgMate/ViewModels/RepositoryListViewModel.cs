using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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

public partial class RepositoryListViewModel : ViewModelBase
{
    public ObservableCollection<BorgRepository> Repositories { get; } = [];

    [ObservableProperty]
    private BorgRepository? _selectedRepository;

    partial void OnSelectedRepositoryChanging(BorgRepository? oldValue, BorgRepository? newValue)
    {
        if (oldValue is not null)
            oldValue.PropertyChanged -= OnSelectedRepoPropertyChanged;
        if (newValue is not null)
            newValue.PropertyChanged += OnSelectedRepoPropertyChanged;
    }

    partial void OnSelectedRepositoryChanged(BorgRepository? value)
    {
        OnPropertyChanged(nameof(CanEditOrRemove));
        OnPropertyChanged(nameof(CanRunBackup));
        Stats.Clear();
        UpdateActiveJob();
    }

    private void OnSelectedRepoPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BorgRepository.IsBusy))
        {
            OnPropertyChanged(nameof(CanEditOrRemove));
            OnPropertyChanged(nameof(CanRunBackup));
            UpdateActiveJob();
        }
    }

    public bool CanEditOrRemove => SelectedRepository is not null && !SelectedRepository.IsBusy;
    public bool CanRunBackup => SelectedRepository is not null && !SelectedRepository.IsBusy && SelectedRepository.SourceDirectories.Count > 0;

    private readonly BorgServiceFactory _borgServiceFactory = null!;
    private readonly IConfigService _configService = null!;
    private readonly IStatusService _statusService = null!;
    private readonly IFilePickerService _filePicker = null!;
    private readonly IJournalService _journalService = null!;
    private readonly BorgOperationRunner _runner = null!;
    private readonly PassphrasePrompt _passphrase = null!;
    private readonly WslHelper _wsl = null!;
    private readonly DirectorySizeCalculator _sizeCalculator = null!;
    private readonly JobQueueService? _jobQueue;
    private readonly ILogger<RepositoryListViewModel> _logger = null!;

    public RepositoryListViewModel() { }

    public RepositoryListViewModel(BorgServiceFactory borgServiceFactory, IConfigService configService, IStatusService statusService, IFilePickerService filePicker, IJournalService journalService, BorgOperationRunner runner, PassphrasePrompt passphrase, WslHelper wsl, DirectorySizeCalculator sizeCalculator, JobQueueService jobQueue, ILogger<RepositoryListViewModel> logger)
    {
        _borgServiceFactory = borgServiceFactory;
        _configService = configService;
        _statusService = statusService;
        _filePicker = filePicker;
        _journalService = journalService;
        _runner = runner;
        _passphrase = passphrase;
        _wsl = wsl;
        _sizeCalculator = sizeCalculator;
        _jobQueue = jobQueue;
        _logger = logger;
        Strings.LanguageChanged += Stats.Reformat;
        Strings.LanguageChanged += () =>
        {
            foreach (var repo in Repositories)
                repo.RefreshScheduleDisplay();
        };
    }

    [RelayCommand]
    private async Task AddNew()
    {
        var vm = RepositoryEditorViewModel.ForNew(_borgServiceFactory, _statusService, _filePicker);
        var window = new RepositoryEditorWindow { DataContext = vm };
        var parent = DialogHelper.GetMainWindow();
        if (parent is not null)
            await window.ShowDialog(parent);

        if (vm.IsSaved)
            await InitializeNewRepository(vm.Repository);
    }

    private async Task InitializeNewRepository(BorgRepository repo)
    {
        if (_jobQueue is null) return;
        if (repo.EncryptionMode != BorgEncryptionMode.None && string.IsNullOrWhiteSpace(repo.Passphrase))
        {
            if (!await _passphrase.EnsurePassphraseAsync(
                repo.EncryptionMode, repo.Name, repo.Path, p => repo.Passphrase = p))
                return;
        }

        await CopySshKeyIfNeeded(repo);
        var service = _borgServiceFactory.GetService(repo.BorgVersion);

        repo.IsBusy = true;
        var journalEntry = _journalService.Add(JournalEventKind.Create, [repo.Name], repo.Name);
        var job = _jobQueue.Enqueue(
            $"{Strings.Get("Job.Init")}: {repo.Name}",
            async (j, ct, progress) =>
            {
                progress.Report(Strings.Get("Status.InitializingRepo"));
                return await service.InitAsync(repo, ct);
            }, repoPath: repo.Path, journalEntry: journalEntry);
        if (SelectedRepository == repo) SetActiveJob(job);

        var result = await job.Completion.Task;
        repo.IsBusy = false;
        if (result.Success)
        {
            Repositories.Add(repo);
            SelectedRepository = repo;
            _configService.RequestSave();
            _journalService.Complete(journalEntry, JournalResult.Completed);
        }
        else if (result.ErrorType == BorgErrorType.RepositoryAlreadyExists)
        {
            if (await DialogHelper.ConfirmAsync(Strings.Get("RepoExistsOpenInstead")))
            {
                Repositories.Add(repo);
                SelectedRepository = repo;
                _configService.RequestSave();
            }
            _journalService.Complete(journalEntry, JournalResult.Completed);
        }
        else if (result.WasCancelled)
        {
            _journalService.Complete(journalEntry, JournalResult.Cancelled);
        }
        else
        {
            _journalService.Complete(journalEntry, JournalResult.Failed);
        }
    }

    [RelayCommand]
    private async Task OpenExisting()
    {
        var vm = RepositoryEditorViewModel.ForOpen(_borgServiceFactory, _statusService, _filePicker);
        var window = new RepositoryEditorWindow { DataContext = vm };
        var parent = DialogHelper.GetMainWindow();
        if (parent is not null)
            await window.ShowDialog(parent);

        if (vm.IsSaved)
        {
            var repo = vm.Repository;
            await CopySshKeyIfNeeded(repo);
            Repositories.Add(repo);
            SelectedRepository = repo;
            _configService.RequestSave();
        }
    }

    [RelayCommand]
    private async Task EditRepository(BorgRepository repo)
    {
        var vm = RepositoryEditorViewModel.ForEdit(_borgServiceFactory, _statusService, _filePicker, repo);
        var window = new RepositoryEditorWindow { DataContext = vm };
        var parent = DialogHelper.GetMainWindow();
        if (parent is not null)
            await window.ShowDialog(parent);

        await CopySshKeyIfNeeded(repo);
        _configService.RequestSave();
        OnPropertyChanged(nameof(CanRunBackup));
    }

    [RelayCommand]
    private async Task RemoveRepository(BorgRepository repo)
    {
        if (!await DialogHelper.ConfirmAsync(string.Format(Strings.Get("ConfirmDeleteRepo"), repo.Name)))
            return;

        Repositories.Remove(repo);
        if (SelectedRepository == repo)
            SelectedRepository = null;
        _configService.RequestSave();
    }

    public event Action? ArchivesChanged;
    public event Action<string>? BackupCompleted;

    // --- Repo Operation Helper ---

    /// <summary>
    /// Runs a borg command operation on a repository with standard lifecycle:
    /// sets repo.IsBusy, creates a journal entry, enqueues the job, awaits completion,
    /// clears IsBusy, records result in journal, and optionally shows error dialog.
    /// </summary>
    private async Task RunRepoOperation(
        BorgRepository repo,
        JournalEventKind eventKind,
        string jobNameKey,
        Func<BorgJob, CancellationToken, Task<BorgResult>> execute,
        Action<BorgResult>? onSuccess = null,
        bool showErrorDialog = true)
    {
        if (_jobQueue is null) return;
        repo.IsBusy = true;
        var journalEntry = _journalService.Add(eventKind, [repo.Name], repo.Name);

        var job = _jobQueue.Enqueue(
            $"{Strings.Get(jobNameKey)}: {repo.Name}",
            async (j, ct, progress) => await execute(j, ct),
            repoPath: repo.Path, journalEntry: journalEntry);
        if (SelectedRepository == repo) SetActiveJob(job);

        var result = await job.Completion.Task;
        repo.IsBusy = false;
        repo.Passphrase = string.Empty;

        if (result.Success)
        {
            _journalService.Complete(journalEntry, JournalResult.Completed);
            onSuccess?.Invoke(result);
        }
        else if (result.WasCancelled)
        {
            _journalService.Complete(journalEntry, JournalResult.Cancelled);
        }
        else
        {
            _journalService.Complete(journalEntry, JournalResult.Failed);
            if (showErrorDialog)
                _statusService.SetError(result.ErrorMessage ?? "", repo.Name, repo.Path);
        }
    }

    private void RefreshAfterModification(BorgRepository repo)
    {
        ArchivesChanged?.Invoke();
        if (SelectedRepository == repo)
            _ = FetchStatsCommand.ExecuteAsync(null);
    }

    // --- Backup ---

    [RelayCommand]
    private async Task RunBackup()
    {
        if (SelectedRepository is null || SelectedRepository.SourceDirectories.Count == 0)
            return;

        var repo = SelectedRepository;
        if (repo.IsBusy) return;

        if (!await _passphrase.EnsurePassphraseAsync(repo))
            return;

        await RunBackupForRepo(repo);
    }

    public async Task RunBackupForRepo(BorgRepository repo)
    {
        if (repo.IsBusy || repo.SourceDirectories.Count == 0) return;

        var archiveName = $"{repo.Name}-{DateTime.Now:yyyy-MM-ddTHH-mm-ss}";
        var service = _borgServiceFactory.GetService(repo.BorgVersion);

        await RunRepoOperation(repo, JournalEventKind.Backup, "Job.Backup",
            async (j, ct) =>
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
            onSuccess: _ =>
            {
                repo.LastBackupAt = DateTime.Now;
                _configService.RequestSave();
                BackupCompleted?.Invoke(repo.Path);
                RefreshAfterModification(repo);
            });
    }

    // --- Maintenance Operations (Prune, Check, Compact) ---

    /// <summary>
    /// Shared flow for maintenance operations (prune/check/compact):
    /// optional confirmation dialog → passphrase prompt → resolve IBorgService →
    /// run with transient retry → RunRepoOperation lifecycle.
    /// </summary>
    private async Task RunMaintenanceOperation(
        string? confirmKey,
        JournalEventKind eventKind,
        string jobNameKey,
        Func<IBorgService, BorgRepository, BorgJob, CancellationToken, Task<BorgResult>> borgOperation,
        bool refreshOnSuccess = false)
    {
        if (SelectedRepository is null) return;
        if (confirmKey is not null && !await DialogHelper.ConfirmAsync(
                string.Format(Strings.Get(confirmKey), SelectedRepository.Name)))
            return;
        if (!await _passphrase.EnsurePassphraseAsync(SelectedRepository))
            return;

        var repo = SelectedRepository;
        var service = _borgServiceFactory.GetService(repo.BorgVersion);

        await RunRepoOperation(repo, eventKind, jobNameKey,
            async (j, ct) => await _runner.RunWithTransientRetry(j, () => borgOperation(service, repo, j, ct)),
            onSuccess: refreshOnSuccess ? _ => RefreshAfterModification(repo) : null);
    }

    [RelayCommand]
    private Task PruneRepo() => RunMaintenanceOperation(
        "ConfirmPrune", JournalEventKind.Prune, "Job.Prune",
        (svc, repo, j, ct) =>
        {
            j.StatusMessage = string.Format(Strings.Get("Status.Pruning"), repo.Name);
            return svc.PruneAsync(repo, ct);
        }, refreshOnSuccess: true);

    [RelayCommand]
    private Task CheckRepo() => RunMaintenanceOperation(
        null, JournalEventKind.Check, "Job.Check",
        (svc, repo, j, ct) =>
        {
            j.StatusMessage = Strings.Get("Status.CheckingProgress");
            return svc.CheckAsync(repo, ct, onStderrLine: line => BorgProgressParser.Update(j, line));
        });

    [RelayCommand]
    private Task CompactRepo() => RunMaintenanceOperation(
        "ConfirmCompact", JournalEventKind.Compact, "Job.Compact",
        (svc, repo, j, ct) =>
        {
            var label = Strings.Get("Status.CompactingProgress");
            j.StatusMessage = label;
            return svc.CompactAsync(repo, ct, onStderrLine: line => BorgProgressParser.Update(j, line, label));
        }, refreshOnSuccess: true);

    // --- Active Job for Detail Panel ---

    public JobProgressTracker Progress { get; } = new();

    public void SetActiveJob(BorgJob? job) => Progress.SetActiveJob(job);

    /// <summary>
    /// Searches the job queue for a running/pending job matching the selected repo
    /// and binds it to the detail panel progress tracker.
    /// </summary>
    private void UpdateActiveJob()
    {
        if (SelectedRepository is { IsBusy: true } repo && _jobQueue is not null)
        {
            foreach (var job in _jobQueue.Jobs)
            {
                if (job.RepoPath == repo.Path && job.Kind == BorgJobKind.Command
                    && job.Status is BorgJobStatus.Running or BorgJobStatus.Pending)
                {
                    SetActiveJob(job);
                    return;
                }
            }
        }
        SetActiveJob(null);
    }

    // --- Repository Statistics ---

    public RepoStatsTracker Stats { get; } = new();

    [RelayCommand]
    private async Task FetchStats()
    {
        if (SelectedRepository is null || _jobQueue is null) return;

        var repo = SelectedRepository;
        var service = _borgServiceFactory.GetService(repo.BorgVersion);

        Stats.IsLoading = true;
        try
        {
            var job = _jobQueue.Enqueue(
                $"{Strings.Get("Statistics")}: {repo.Name}",
                async (j, ct, progress) => await _runner.RunWithPassphraseRetry(
                    repo, () => _runner.RunWithTransientRetry(j,
                        () => service.InfoRepoAsync(repo, ct))),
                BorgJobKind.Query, $"stats:{repo.Path}", repo.Path);
            var result = await job.Completion.Task;

            if (result.Success && SelectedRepository == repo)
                Stats.ParseRepoInfo(result.StandardOutput, _logger);
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Failed to fetch repo stats"); }
        finally
        {
            Stats.IsLoading = false;
        }
    }

    private async Task CopySshKeyIfNeeded(BorgRepository repo)
    {
        if (WslHelper.IsRequired && !repo.IsLocal && !string.IsNullOrWhiteSpace(repo.SshKeyPath))
            await _wsl.CopySshKeyAsync(repo.SshKeyPath);
    }
}
