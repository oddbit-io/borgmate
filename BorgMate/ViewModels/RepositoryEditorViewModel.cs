using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using BorgMate.Models;
using BorgMate.Services;
using BorgMate.Services.Borg;
using BorgMate.Services.Journal;
using BorgMate.Services.Queue;
using BorgMate.Services.UI;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BorgMate.ViewModels;

/// <summary>
/// Edit model for creating, opening, or editing a <see cref="BorgRepository"/>.
/// All editable state lives on the VM itself as observable properties — the AXAML
/// binds to VM fields directly, not to any BorgRepository. No live domain instance
/// is mutated until the user clicks Save AND the borg verification (init or info)
/// succeeds; at that point <see cref="CommitResult"/> either applies the VM state
/// to the Edit target or exposes a freshly-built BorgRepository via
/// <see cref="Repository"/> for the caller to add to the repository list.
/// </summary>
public partial class RepositoryEditorViewModel : ViewModelBase, ISaveable
{
    private readonly BorgServiceFactory? _borgServiceFactory;
    private readonly IFilePickerService? _filePicker;
    private readonly JobQueueService? _jobQueue;
    private readonly IJournalService? _journalService;
    private readonly PassphrasePrompt? _passphrase;
    private readonly BorgOperationRunner? _runner;
    private readonly WslHelper? _wsl;

    /// <summary>The live instance being edited (Edit mode only). Null for Create/Open.</summary>
    private BorgRepository? _targetRepo;

    /// <summary>Snapshot of VM reachability fields at dialog open time (Edit only).</summary>


    public RepositoryEditorViewModel() { }

    public RepositoryEditorViewModel(
        BorgServiceFactory borgServiceFactory,
        IFilePickerService filePicker,
        JobQueueService? jobQueue = null,
        IJournalService? journalService = null,
        PassphrasePrompt? passphrase = null,
        BorgOperationRunner? runner = null,
        WslHelper? wsl = null)
    {
        _borgServiceFactory = borgServiceFactory;
        _filePicker = filePicker;
        _jobQueue = jobQueue;
        _journalService = journalService;
        _passphrase = passphrase;
        _runner = runner;
        _wsl = wsl;
    }

    /// <summary>
    /// Result of a successful save. Non-null only after <see cref="IsSaved"/> is true.
    /// For Create/Open this is a newly-built BorgRepository; for Edit it's the same
    /// live instance that was passed into <see cref="ForEdit"/>, now updated in place.
    /// </summary>
    public BorgRepository? Repository { get; private set; }

    // --- Edit model: repository identity ---

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _archiveNamePrefix = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSsh))]
    private bool _isLocal = true;

    public bool IsSsh
    {
        get => !IsLocal;
        set => IsLocal = !value;
    }

    // SSH connection fields (user@host:port). RepoPath is the trailing path
    // component — combined into a full "user@host:/path" at commit time.

    [ObservableProperty]
    private string _sshHost = string.Empty;

    [ObservableProperty]
    private string _sshUser = string.Empty;

    [ObservableProperty]
    private int _sshPort = 22;

    [ObservableProperty]
    private string _sshKeyPath = string.Empty;

    [ObservableProperty]
    private string _repoPath = string.Empty;

    // --- Edit model: borg / advanced ---

    [ObservableProperty]
    private BorgVersion _borgVersion = BorgVersion.Borg1;

    [ObservableProperty]
    private BorgEncryptionMode _encryptionMode = BorgEncryptionMode.RepokeyBlake2;

    [ObservableProperty]
    private string _borgRemotePath = string.Empty;

    [ObservableProperty]
    private int _rateLimit;

    // Source directories and schedule.
    public ObservableCollection<string> SourceDirectories { get; } = [];

    [ObservableProperty]
    private string? _selectedDirectory;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsScheduled))]
    private BackupMode _mode = BackupMode.Manual;

    public bool IsScheduled
    {
        get => Mode == BackupMode.Scheduled;
        set => Mode = value ? BackupMode.Scheduled : BackupMode.Manual;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowHourMinute))]
    [NotifyPropertyChangedFor(nameof(ShowDayOfWeek))]
    [NotifyPropertyChangedFor(nameof(ShowDayOfMonth))]
    [NotifyPropertyChangedFor(nameof(ShowInterval))]
    private ScheduleFrequency _selectedFrequency = ScheduleFrequency.Daily;

    public bool ShowHourMinute => SelectedFrequency is not ScheduleFrequency.EveryNHours;
    public bool ShowDayOfWeek => SelectedFrequency == ScheduleFrequency.Weekly;
    public bool ShowDayOfMonth => SelectedFrequency == ScheduleFrequency.Monthly;
    public bool ShowInterval => SelectedFrequency == ScheduleFrequency.EveryNHours;

    public ScheduleFrequency[] Frequencies { get; } = System.Enum.GetValues<ScheduleFrequency>();
    public System.DayOfWeek[] DaysOfWeek { get; } = System.Enum.GetValues<System.DayOfWeek>();

    [ObservableProperty]
    private int _scheduleHour = 2;

    [ObservableProperty]
    private int _scheduleMinute;

    [ObservableProperty]
    private System.DayOfWeek _selectedDayOfWeek = System.DayOfWeek.Monday;

    [ObservableProperty]
    private int _scheduleDayOfMonth = 1;

    [ObservableProperty]
    private int _intervalHours = 6;

    [ObservableProperty]
    private bool _runMissed = true;

    [ObservableProperty]
    private bool _runPruneAfterBackup;

    // --- Edit model: prune retention (enabled flag + count per rule) ---
    // The count keeps its defaulted value while the checkbox is off so re-enabling
    // restores the user's previous number instead of snapping to zero. On ApplyTo,
    // a disabled rule is written as 0 — PruneOptions.HasAnyRetention gates persistence.

    [ObservableProperty]
    private bool _keepLastEnabled;

    [ObservableProperty]
    private int _keepLast = 3;

    [ObservableProperty]
    private bool _keepHourlyEnabled;

    [ObservableProperty]
    private int _keepHourly = 24;

    [ObservableProperty]
    private bool _keepDailyEnabled;

    [ObservableProperty]
    private int _keepDaily = 7;

    [ObservableProperty]
    private bool _keepWeeklyEnabled;

    [ObservableProperty]
    private int _keepWeekly = 4;

    [ObservableProperty]
    private bool _keepMonthlyEnabled;

    [ObservableProperty]
    private int _keepMonthly = 6;

    [ObservableProperty]
    private bool _keepYearlyEnabled;

    [ObservableProperty]
    private int _keepYearly = 2;

    [ObservableProperty]
    private bool _compactAfterPrune = true;

    // --- Mode flags and dialog state ---

    [ObservableProperty]
    private bool _isNew;

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private bool _isSaved;

    [ObservableProperty]
    private string? _pathError;

    [ObservableProperty]
    private string? _nameError;

    [ObservableProperty]
    private string? _sshKeyError;

    // --- Verification state (borg call in flight from Save) ---

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyPropertyChangedFor(nameof(VerifyMessage))]
    [NotifyPropertyChangedFor(nameof(HasVerifyMessage))]
    [NotifyPropertyChangedFor(nameof(VerifyMessageIsError))]
    private bool _isVerifying;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VerifyMessage))]
    [NotifyPropertyChangedFor(nameof(HasVerifyMessage))]
    [NotifyPropertyChangedFor(nameof(VerifyMessageIsError))]
    private string? _verifyError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VerifyMessage))]
    [NotifyPropertyChangedFor(nameof(HasVerifyMessage))]
    private string? _verifyStatus;

    /// <summary>
    /// Single-line status shown next to the spinner: the current VerifyStatus
    /// while verifying, otherwise the first line of VerifyError (if set), otherwise
    /// the last VerifyStatus. Full error is exposed via tooltip on the row.
    /// </summary>
    public string? VerifyMessage
    {
        get
        {
            if (IsVerifying) return VerifyStatus;
            if (!string.IsNullOrEmpty(VerifyError))
            {
                var nl = VerifyError.IndexOfAny(['\r', '\n']);
                return nl < 0 ? VerifyError : VerifyError[..nl];
            }
            return VerifyStatus;
        }
    }

    public bool HasVerifyMessage => !string.IsNullOrEmpty(VerifyMessage);
    public bool VerifyMessageIsError => !IsVerifying && !string.IsNullOrEmpty(VerifyError);

    public string Title => IsNew ? Strings.Get("CreateNewRepo")
        : IsOpen ? Strings.Get("OpenExistingRepo")
        : Strings.Get("EditRepo");

    public string SaveButtonText => IsNew ? Strings.Get("Create")
        : IsOpen ? Strings.Get("Open")
        : Strings.Get("Save");

    public BorgVersion[] BorgVersions { get; } = System.Enum.GetValues<BorgVersion>();

    public BorgEncryptionMode[] BorgEncryptionModes { get; } = System.Enum.GetValues<BorgEncryptionMode>();

    // --- Commands: browsing and source dirs ---

    [RelayCommand]
    private async Task BrowseSourceDirectories()
    {
        var paths = await _filePicker!.PickFoldersAsync(Strings.Get("Picker.SelectSourceDirs"));
        foreach (var path in paths)
            if (!SourceDirectories.Contains(path))
                SourceDirectories.Add(path);
    }

    [RelayCommand]
    private void RemoveSourceDirectory(string path) => SourceDirectories.Remove(path);

    [RelayCommand]
    private async Task BrowseRepoPath()
    {
        var path = await _filePicker!.PickFolderAsync(Strings.Get("Picker.SelectRepository"));
        if (path is not null)
            RepoPath = path;
    }

    [RelayCommand]
    private async Task BrowseSshKey()
    {
        var path = await _filePicker!.PickFileAsync(Strings.Get("Picker.SelectSshKey"));
        if (path is not null)
            SshKeyPath = path;
    }

    // --- Factories ---

    /// <summary>Creates a VM for adding a new repository (Create button + borg init).</summary>
    public static RepositoryEditorViewModel ForNew(
        BorgServiceFactory factory, IFilePickerService filePicker,
        JobQueueService? jobQueue = null, IJournalService? journalService = null,
        PassphrasePrompt? passphrase = null, BorgOperationRunner? runner = null,
        WslHelper? wsl = null)
    {
        return new RepositoryEditorViewModel(factory, filePicker, jobQueue, journalService, passphrase, runner, wsl)
        {
            IsNew = true,
        };
    }

    /// <summary>Creates a VM for opening an existing borg repo (Open button + borg info).</summary>
    public static RepositoryEditorViewModel ForOpen(
        BorgServiceFactory factory, IFilePickerService filePicker,
        JobQueueService? jobQueue = null, IJournalService? journalService = null,
        PassphrasePrompt? passphrase = null, BorgOperationRunner? runner = null,
        WslHelper? wsl = null)
    {
        return new RepositoryEditorViewModel(factory, filePicker, jobQueue, journalService, passphrase, runner, wsl)
        {
            IsOpen = true,
        };
    }

    /// <summary>
    /// Creates a VM for editing an existing repository. The VM loads the repo's
    /// current state into its own edit fields; the live <paramref name="repo"/>
    /// is held as the target and is only mutated by <see cref="Save"/> after
    /// verification succeeds.
    /// </summary>
    public static RepositoryEditorViewModel ForEdit(
        BorgServiceFactory factory, IFilePickerService filePicker, BorgRepository repo,
        JobQueueService? jobQueue = null, IJournalService? journalService = null,
        PassphrasePrompt? passphrase = null, BorgOperationRunner? runner = null,
        WslHelper? wsl = null)
    {
        var vm = new RepositoryEditorViewModel(factory, filePicker, jobQueue, journalService, passphrase, runner, wsl);
        vm.LoadFrom(repo);
        vm._targetRepo = repo;
        return vm;
    }

    /// <summary>Copies <paramref name="repo"/>'s persistable fields into this VM.</summary>
    private void LoadFrom(BorgRepository repo)
    {
        Name = repo.Name;
        ArchiveNamePrefix = repo.ArchiveNamePrefix;
        IsLocal = repo.IsLocal;
        BorgVersion = repo.BorgVersion;
        EncryptionMode = repo.EncryptionMode;
        SshKeyPath = repo.SshKeyPath;
        SshPort = repo.SshPort;
        BorgRemotePath = repo.BorgRemotePath;
        RateLimit = repo.RateLimit;
        Mode = repo.Mode;

        SelectedFrequency = repo.Schedule.Frequency;
        ScheduleHour = repo.Schedule.Hour;
        ScheduleMinute = repo.Schedule.Minute;
        SelectedDayOfWeek = repo.Schedule.DayOfWeek;
        ScheduleDayOfMonth = repo.Schedule.DayOfMonth;
        IntervalHours = repo.Schedule.IntervalHours;
        RunMissed = repo.Schedule.RunMissed;
        RunPruneAfterBackup = repo.Schedule.RunPruneAfterBackup;

        SourceDirectories.Clear();
        foreach (var dir in repo.SourceDirectories)
            SourceDirectories.Add(dir);

        KeepLastEnabled = repo.PruneOptions.KeepLast > 0;
        if (repo.PruneOptions.KeepLast > 0) KeepLast = repo.PruneOptions.KeepLast;
        KeepHourlyEnabled = repo.PruneOptions.KeepHourly > 0;
        if (repo.PruneOptions.KeepHourly > 0) KeepHourly = repo.PruneOptions.KeepHourly;
        KeepDailyEnabled = repo.PruneOptions.KeepDaily > 0;
        if (repo.PruneOptions.KeepDaily > 0) KeepDaily = repo.PruneOptions.KeepDaily;
        KeepWeeklyEnabled = repo.PruneOptions.KeepWeekly > 0;
        if (repo.PruneOptions.KeepWeekly > 0) KeepWeekly = repo.PruneOptions.KeepWeekly;
        KeepMonthlyEnabled = repo.PruneOptions.KeepMonthly > 0;
        if (repo.PruneOptions.KeepMonthly > 0) KeepMonthly = repo.PruneOptions.KeepMonthly;
        KeepYearlyEnabled = repo.PruneOptions.KeepYearly > 0;
        if (repo.PruneOptions.KeepYearly > 0) KeepYearly = repo.PruneOptions.KeepYearly;
        CompactAfterPrune = repo.PruneOptions.CompactAfterPrune;

        DecomposePath(repo.Path);
    }

    /// <summary>Writes current VM fields onto <paramref name="repo"/> (schedule + source dirs + config).</summary>
    private void ApplyTo(BorgRepository repo)
    {
        repo.Name = Name;
        repo.ArchiveNamePrefix = ArchiveNamePrefix;
        repo.IsLocal = IsLocal;
        repo.Path = ComposePath();
        repo.BorgVersion = BorgVersion;
        repo.EncryptionMode = EncryptionMode;
        repo.SshKeyPath = SshKeyPath;
        repo.SshPort = SshPort;
        repo.BorgRemotePath = BorgRemotePath;
        repo.RateLimit = RateLimit;
        repo.Mode = Mode;

        repo.Schedule.Frequency = SelectedFrequency;
        repo.Schedule.Hour = ScheduleHour;
        repo.Schedule.Minute = ScheduleMinute;
        repo.Schedule.DayOfWeek = SelectedDayOfWeek;
        repo.Schedule.DayOfMonth = ScheduleDayOfMonth;
        repo.Schedule.IntervalHours = IntervalHours;

        if (!repo.Schedule.RunMissed && RunMissed)
        {
            repo.LastBackupAt = DateTime.Now;
        }
        
        repo.Schedule.RunMissed = RunMissed;
        repo.Schedule.RunPruneAfterBackup = RunPruneAfterBackup;

        repo.SourceDirectories.Clear();
        foreach (var dir in SourceDirectories)
            repo.SourceDirectories.Add(dir);

        repo.PruneOptions.KeepLast = KeepLastEnabled ? KeepLast : 0;
        repo.PruneOptions.KeepHourly = KeepHourlyEnabled ? KeepHourly : 0;
        repo.PruneOptions.KeepDaily = KeepDailyEnabled ? KeepDaily : 0;
        repo.PruneOptions.KeepWeekly = KeepWeeklyEnabled ? KeepWeekly : 0;
        repo.PruneOptions.KeepMonthly = KeepMonthlyEnabled ? KeepMonthly : 0;
        repo.PruneOptions.KeepYearly = KeepYearlyEnabled ? KeepYearly : 0;
        repo.PruneOptions.CompactAfterPrune = CompactAfterPrune;

        repo.RefreshScheduleDisplay();
    }

    /// <summary>Builds a fresh BorgRepository reflecting the current VM state.</summary>
    private BorgRepository BuildRepo()
    {
        var repo = new BorgRepository();
        ApplyTo(repo);
        return repo;
    }

    /// <summary>Splits a "user@host:/path" path (or a local path) into VM fields.</summary>
    private void DecomposePath(string path)
    {
        if (IsLocal)
        {
            RepoPath = path;
            return;
        }

        var colonIdx = path.IndexOf(':');
        if (colonIdx < 0)
        {
            RepoPath = path;
            return;
        }

        var hostPart = path[..colonIdx];
        RepoPath = path[(colonIdx + 1)..];

        var atIdx = hostPart.IndexOf('@');
        if (atIdx >= 0)
        {
            SshUser = hostPart[..atIdx];
            SshHost = hostPart[(atIdx + 1)..];
        }
        else
        {
            SshHost = hostPart;
        }
    }

    /// <summary>Builds the full repo path from <see cref="IsLocal"/>, host, user, and <see cref="RepoPath"/>.</summary>
    internal string ComposePath()
    {
        if (IsLocal) return RepoPath;
        var userPart = string.IsNullOrWhiteSpace(SshUser) ? "" : $"{SshUser.Trim()}@";
        return $"{userPart}{SshHost.Trim()}:{RepoPath.Trim()}";
    }

    // --- Reachability check (Edit mode: skip borg info when nothing relevant changed) ---

    /// <summary>
    // --- Diagnostic buttons (Check Version / Check Remote Path) ---

    [RelayCommand]
    private async Task CheckVersion()
    {
        var service = _borgServiceFactory!.GetService(BorgVersion);
        VerifyError = null;
        VerifyStatus = Strings.Get("Status.CheckingVersion");
        var result = await service.GetVersionAsync();
        if (result.Success)
            VerifyStatus = result.StandardOutput.Trim();
        else
        {
            VerifyStatus = null;
            VerifyError = result.ErrorMessage;
        }
    }

    [RelayCommand]
    private async Task CheckRemotePath()
    {
        if (string.IsNullOrWhiteSpace(BorgRemotePath)) return;

        var userPart = string.IsNullOrWhiteSpace(SshUser) ? "" : $"{SshUser.Trim()}@";
        var host = $"{userPart}{SshHost.Trim()}";
        var service = _borgServiceFactory!.GetService(BorgVersion);
        VerifyError = null;
        VerifyStatus = Strings.Get("Status.CheckingRemotePath");
        var result = await service.CheckRemotePathAsync(host, BorgRemotePath, SshKeyPath);
        if (result.Success)
            VerifyStatus = Strings.Get("Status.RemotePathOk");
        else
        {
            VerifyStatus = null;
            VerifyError = result.ErrorMessage;
        }
    }

    // --- Save ---

    private bool CanSave() => !IsVerifying;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task Save()
    {
        if (!ValidateFields()) return;

        // Build a transient BorgRepository from the current VM state. This instance
        // is passed to the borg verify call; on success it becomes the persistent
        // result for Create/Open, or the source of the ApplyTo(_targetRepo) commit
        // for Edit. On failure, it's discarded — the VM and _targetRepo are untouched.
        var candidate = BuildRepo();

        // Design-time or test path with no borg deps: accept immediately.
        if (_jobQueue is null || _journalService is null || _passphrase is null || _runner is null || _borgServiceFactory is null)
        {
            CommitResult(candidate);
            return;
        }

        VerifyError = null;
        IsVerifying = true;
        try
        {
            await CopySshKeyIfNeededAsync();

            if (IsNew)
                await VerifyByInitializingAsync(candidate);
            else
                // Open and Edit both verify via `borg info` against the candidate.
                await VerifyByInfoAsync(candidate);
        }
        finally
        {
            IsVerifying = false;
            VerifyStatus = null;
        }
    }

    private bool ValidateFields()
    {
        PathError = null;
        NameError = null;
        SshKeyError = null;

        var valid = true;

        if (string.IsNullOrWhiteSpace(RepoPath))
        {
            PathError = Strings.Get("ValidationRequired");
            valid = false;
        }

        if (IsSsh && string.IsNullOrWhiteSpace(SshHost))
        {
            PathError = Strings.Get("ValidationRequired");
            valid = false;
        }

        if (IsSsh && string.IsNullOrWhiteSpace(SshKeyPath))
        {
            SshKeyError = Strings.Get("ValidationRequired");
            valid = false;
        }

        if (string.IsNullOrWhiteSpace(Name))
        {
            NameError = Strings.Get("ValidationRequired");
            valid = false;
        }

        return valid;
    }

    private async Task CopySshKeyIfNeededAsync()
    {
        if (_wsl is not null && WslHelper.IsRequired && !IsLocal && !string.IsNullOrWhiteSpace(SshKeyPath))
            await _wsl.CopySshKeyAsync(SshKeyPath);
    }

    /// <summary>
    /// Commits a successful save. For Edit: applies current VM state to the live
    /// target repo (in place, so caller references stay valid) and carries any
    /// passphrase set during verification over from the candidate. For Create/Open:
    /// exposes the candidate as the result for the caller to add to the list.
    /// Then flips IsSaved, which auto-closes the dialog via ModalWindow's ISaveable hook.
    /// </summary>
    private void CommitResult(BorgRepository candidate)
    {
        if (_targetRepo is not null)
        {
            ApplyTo(_targetRepo);
            if (!string.IsNullOrEmpty(candidate.Passphrase))
                _targetRepo.Passphrase = candidate.Passphrase;
            Repository = _targetRepo;
        }
        else
        {
            Repository = candidate;
        }
        IsSaved = true;
    }

    /// <summary>
    /// Initializes a new borg repository on <paramref name="candidate"/>. On success,
    /// commits the result. On "already exists", prompts the user to open it instead.
    /// On any other failure, sets VerifyError and leaves the dialog open.
    /// </summary>
    private async Task VerifyByInitializingAsync(BorgRepository candidate)
    {
        if (candidate.EncryptionMode != BorgEncryptionMode.None && string.IsNullOrWhiteSpace(candidate.Passphrase))
        {
            if (!await _passphrase!.EnsurePassphraseAsync(
                candidate.EncryptionMode, candidate.Name, candidate.Path,
                p => candidate.Passphrase = p))
                return; // user cancelled; dialog stays open
        }

        var service = _borgServiceFactory!.GetService(candidate.BorgVersion);
        var journalEntry = _journalService!.Add(JournalEventKind.Create, [candidate.Name], candidate.Name);

        VerifyStatus = Strings.Get("Status.InitializingRepo");
        var job = _jobQueue!.Enqueue(
            $"{Strings.Get("Job.Init")}: {candidate.Name}",
            async (j, ct, progress) =>
            {
                progress.Report(Strings.Get("Status.InitializingRepo"));
                return await service.InitAsync(candidate, ct);
            },
            repoPath: candidate.Path, journalEntry: journalEntry);

        var result = await job.Completion.Task;

        if (result.Success)
        {
            _journalService.Complete(journalEntry, JournalResult.Completed);
            CommitResult(candidate);
            return;
        }

        if (result.ErrorType == BorgErrorType.RepositoryAlreadyExists)
        {
            _journalService.Complete(journalEntry, JournalResult.Completed);
            if (await DialogHelper.ConfirmAsync(Strings.Get("RepoExistsOpenInstead")))
            {
                CommitResult(candidate);
                return;
            }
            VerifyError = Strings.Get("Error.RepositoryAlreadyExists");
            return;
        }

        if (result.WasCancelled)
        {
            _journalService.Complete(journalEntry, JournalResult.Cancelled);
            return;
        }

        _journalService.Complete(journalEntry, JournalResult.Failed, result.ErrorMessage);
        VerifyError = result.ErrorMessage;
        // Clear passphrase on candidate so the next Save re-prompts.
        candidate.Passphrase = string.Empty;
    }

    /// <summary>
    /// Verifies <paramref name="candidate"/> by running `borg info`, which proves
    /// both reachability and credential validity. Commits the result on success;
    /// sets VerifyError and keeps the dialog open on failure.
    /// </summary>
    private async Task VerifyByInfoAsync(BorgRepository candidate)
    {
        var service = _borgServiceFactory!.GetService(candidate.BorgVersion);

        VerifyStatus = Strings.Get("Status.VerifyingRepo");
        var job = _jobQueue!.Enqueue(
            $"{Strings.Get("Job.VerifyRepo")}: {candidate.Name}",
            async (j, ct, progress) =>
            {
                progress.Report(Strings.Get("Status.VerifyingRepo"));
                return await _runner!.RunWithPassphraseRetry(candidate,
                    () => service.InfoRepoAsync(candidate, ct));
            },
            BorgJobKind.Query, repoPath: candidate.Path);

        var result = await job.Completion.Task;

        if (result.Success)
        {
            CommitResult(candidate);
            return;
        }

        if (result.WasCancelled)
            return;

        VerifyError = result.ErrorMessage;
        candidate.Passphrase = string.Empty;
    }
}
