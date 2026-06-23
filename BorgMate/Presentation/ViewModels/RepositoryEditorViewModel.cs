using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using BorgMate.Domain.Repositories;
using BorgMate.Infrastructure.Borg;
using BorgMate.Presentation.Dialogs;
using BorgMate.Runtime.Operations;
using BorgMate.Runtime.Orchestration;
using BorgMate.Runtime.Progress;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BorgMate.Presentation.ViewModels;

/// <summary>
///     Edit model for creating, opening, or editing a repository.
///     All editable state lives on the VM itself as observable properties — the AXAML
///     binds to VM fields directly, not to any BorgRepository. No live domain instance
///     is mutated until the user clicks Save AND the borg verification (init or info)
///     succeeds; at that point <see cref="CommitResult" /> either applies the VM state
///     to the Edit target or exposes a freshly-built repository wrapper via
///     <see cref="Repository" /> for the caller to add to the repository list.
/// </summary>
public partial class RepositoryEditorViewModel : ViewModelBase, ISaveable
{
    private readonly IFilePickerService? _filePicker;
    private readonly IRepositoryVerificationOperationRunner? _verificationRunner;

    [ObservableProperty]
    private string _archiveNamePrefix = string.Empty;

    [ObservableProperty]
    private string _borgRemotePath = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBorgUrlSshLike))]
    [NotifyPropertyChangedFor(nameof(ShowSshKeyOptions))]
    [NotifyPropertyChangedFor(nameof(ShowRemoteBorgPathOptions))]
    private string _borgUrl = string.Empty;

    // --- Edit model: borg / advanced ---

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBorg2))]
    [NotifyPropertyChangedFor(nameof(CanUseBorgUrl))]
    [NotifyPropertyChangedFor(nameof(ShowBorg1EncryptionMode))]
    [NotifyPropertyChangedFor(nameof(ShowBorg2EncryptionMode))]
    private BorgVersion _borgVersion = BorgVersion.Borg1;

    [ObservableProperty]
    private Borg2EncryptionMode _borg2EncryptionMode = Borg2EncryptionMode.RepokeyAesOcb;

    [ObservableProperty]
    private bool _compactAfterPrune = true;

    [ObservableProperty]
    private BorgEncryptionMode _encryptionMode = BorgEncryptionMode.RepokeyBlake2;

    [ObservableProperty]
    private int _intervalHours = 6;

    [ObservableProperty]
    private bool _isDuplicate;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSsh))]
    [NotifyPropertyChangedFor(nameof(ShowSshKeyOptions))]
    [NotifyPropertyChangedFor(nameof(ShowRemoteBorgPathOptions))]
    private bool _isLocal = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSsh))]
    [NotifyPropertyChangedFor(nameof(IsBorgUrlSshLike))]
    [NotifyPropertyChangedFor(nameof(ShowSshKeyOptions))]
    [NotifyPropertyChangedFor(nameof(ShowRemoteBorgPathOptions))]
    private bool _isBorgUrlLocation;

    // --- Mode flags and dialog state ---

    [ObservableProperty]
    private bool _isNew;

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private bool _isSaved;

    // --- Verification state (borg call in flight from Save) ---

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyPropertyChangedFor(nameof(VerifyMessage))]
    [NotifyPropertyChangedFor(nameof(HasVerifyMessage))]
    [NotifyPropertyChangedFor(nameof(VerifyMessageIsError))]
    private bool _isVerifying;

    [ObservableProperty]
    private int _keepDaily = 7;

    [ObservableProperty]
    private bool _keepDailyEnabled;

    [ObservableProperty]
    private int _keepHourly = 24;

    [ObservableProperty]
    private bool _keepHourlyEnabled;

    [ObservableProperty]
    private int _keepLast = 3;

    // --- Edit model: prune retention (enabled flag + count per rule) ---
    // The count keeps its defaulted value while the checkbox is off so re-enabling
    // restores the user's previous number instead of snapping to zero. On ApplyTo,
    // a disabled rule is written as 0 — PruneOptions.HasAnyRetention gates persistence.

    [ObservableProperty]
    private bool _keepLastEnabled;

    [ObservableProperty]
    private int _keepMonthly = 6;

    [ObservableProperty]
    private bool _keepMonthlyEnabled;

    [ObservableProperty]
    private int _keepWeekly = 4;

    [ObservableProperty]
    private bool _keepWeeklyEnabled;

    [ObservableProperty]
    private int _keepYearly = 2;

    [ObservableProperty]
    private bool _keepYearlyEnabled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsScheduled))]
    private BackupMode _mode = BackupMode.Manual;

    // --- Edit model: repository identity ---

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string? _nameError;

    [ObservableProperty]
    private string? _pathError;

    [ObservableProperty]
    private int _rateLimit;

    /// <summary>
    ///     Snapshot of reachability-affecting fields captured at <see cref="ForEdit" /> time.
    ///     Used by <see cref="NeedsReachabilityCheck" /> to skip the borg info call entirely
    ///     when none of these fields changed (rename / schedule / prune / autoload edits).
    ///     Null for Create/Open/Duplicate, which always verify.
    /// </summary>
    private ReachabilitySnapshot? _reachabilitySnapshot;

    [ObservableProperty]
    private string _repoPath = string.Empty;

    [ObservableProperty]
    private bool _runMissed = true;

    [ObservableProperty]
    private bool _runPruneAfterBackup;

    [ObservableProperty]
    private int _scheduleDayOfMonth = 1;

    [ObservableProperty]
    private int _scheduleHour = 2;

    [ObservableProperty]
    private int _scheduleMinute;

    [ObservableProperty]
    private DayOfWeek _selectedDayOfWeek = DayOfWeek.Monday;

    [ObservableProperty]
    private string? _selectedDirectory;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowHourMinute))]
    [NotifyPropertyChangedFor(nameof(ShowDayOfWeek))]
    [NotifyPropertyChangedFor(nameof(ShowDayOfMonth))]
    [NotifyPropertyChangedFor(nameof(ShowInterval))]
    private ScheduleFrequency _selectedFrequency = ScheduleFrequency.Daily;

    // SSH connection fields (user@host:port). RepoPath is the trailing path
    // component — combined into a full "user@host:/path" at commit time.

    [ObservableProperty]
    private string _sshHost = string.Empty;

    [ObservableProperty]
    private string? _sshKeyError;

    [ObservableProperty]
    private string _sshKeyPath = string.Empty;

    [ObservableProperty]
    private int _sshPort = 22;

    [ObservableProperty]
    private string _sshUser = string.Empty;

    /// <summary>The live instance being edited (Edit mode only). Null for Create/Open.</summary>
    private RepositoryViewModel? _targetRepo;

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
    ///     Carries LastBackupAt through the editor for Edit (round-trips the same value)
    ///     and Duplicate (preserves the source's value so a duplicate of a scheduled
    ///     repo doesn't immediately fire a missed-backup run). Null for Create/Open.
    /// </summary>
    private DateTime? _vmLastBackupAt;


    public RepositoryEditorViewModel()
    {
    }

    public RepositoryEditorViewModel(
        IFilePickerService filePicker,
        IRepositoryVerificationOperationRunner? verificationRunner = null,
        bool isBorg2SupportEnabled = false)
    {
        _filePicker = filePicker;
        _verificationRunner = verificationRunner;
        IsBorg2SupportEnabled = isBorg2SupportEnabled;
    }

    /// <summary>
    ///     Result of a successful save. Non-null only after <see cref="RepositoryEditorViewModel.IsSaved" /> is true.
    ///     For Create/Open this is a newly-built repository wrapper; for Edit it's the same
    ///     live instance that was passed into <see cref="ForEdit" />, now updated in place.
    /// </summary>
    public RepositoryViewModel? Repository { get; private set; }

    public bool IsSsh
    {
        get => !IsLocal && !IsBorgUrlLocation;
        set
        {
            if (!value) return;
            IsBorgUrlLocation = false;
            IsLocal = false;
        }
    }

    public bool IsBorg2 => BorgVersion == BorgVersion.Borg2;
    public bool CanUseBorgUrl => IsBorg2;
    public bool IsBorg2SupportEnabled { get; private set; }
    public bool ShowBorgVersionSelection => IsBorg2SupportEnabled;
    public bool ShowBorg1EncryptionMode => !IsBorg2;
    public bool ShowBorg2EncryptionMode => IsBorg2;
    public bool IsBorgUrlSshLike => IsBorgUrlLocation && BorgRepositoryUrlValidator.UsesSshTransport(BorgUrl);
    public bool ShowSshKeyOptions => IsSsh || IsBorgUrlSshLike;
    public bool ShowRemoteBorgPathOptions => ShowSshKeyOptions;

    // Source directories and schedule.
    public ObservableCollection<string> SourceDirectories { get; } = [];

    public bool IsScheduled
    {
        get => Mode == BackupMode.Scheduled;
        set => Mode = value ? BackupMode.Scheduled : BackupMode.Manual;
    }

    public bool ShowHourMinute => SelectedFrequency is not ScheduleFrequency.EveryNHours;
    public bool ShowDayOfWeek => SelectedFrequency == ScheduleFrequency.Weekly;
    public bool ShowDayOfMonth => SelectedFrequency == ScheduleFrequency.Monthly;
    public bool ShowInterval => SelectedFrequency == ScheduleFrequency.EveryNHours;

    public ScheduleFrequency[] Frequencies { get; } = Enum.GetValues<ScheduleFrequency>();
    public DayOfWeek[] DaysOfWeek { get; } = Enum.GetValues<DayOfWeek>();

    public Borg2EncryptionMode[] Borg2EncryptionModes { get; } = Enum.GetValues<Borg2EncryptionMode>();

    partial void OnIsLocalChanged(bool value)
    {
        if (value)
            IsBorgUrlLocation = false;

        OnPropertyChanged(nameof(IsSsh));
        OnPropertyChanged(nameof(ShowSshKeyOptions));
        OnPropertyChanged(nameof(ShowRemoteBorgPathOptions));
    }

    partial void OnIsBorgUrlLocationChanged(bool value)
    {
        if (value)
            IsLocal = false;

        OnPropertyChanged(nameof(IsSsh));
        OnPropertyChanged(nameof(IsBorgUrlSshLike));
        OnPropertyChanged(nameof(ShowSshKeyOptions));
        OnPropertyChanged(nameof(ShowRemoteBorgPathOptions));
    }

    partial void OnBorgVersionChanged(BorgVersion value)
    {
        if (value != BorgVersion.Borg2 && IsBorgUrlLocation)
            IsLocal = true;

        OnPropertyChanged(nameof(IsBorg2));
        OnPropertyChanged(nameof(CanUseBorgUrl));
        OnPropertyChanged(nameof(ShowBorg1EncryptionMode));
        OnPropertyChanged(nameof(ShowBorg2EncryptionMode));
    }

    /// <summary>
    ///     Single-line status shown next to the spinner: the current VerifyStatus
    ///     while verifying, otherwise the first line of VerifyError (if set), otherwise
    ///     the last VerifyStatus. Full error is exposed via tooltip on the row.
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
        : IsDuplicate ? Strings.Get("DuplicateRepo")
        : Strings.Get("EditRepo");

    public string SaveButtonText => IsNew ? Strings.Get("Create")
        : IsOpen ? Strings.Get("Open")
        : IsDuplicate ? Strings.Get("Create")
        : Strings.Get("Save");

    public BorgVersion[] BorgVersions { get; } = Enum.GetValues<BorgVersion>();

    public BorgEncryptionMode[] BorgEncryptionModes { get; } = Enum.GetValues<BorgEncryptionMode>();

    /// <summary>
    ///     True only in Edit mode when at least one reachability-affecting field has
    ///     diverged from the snapshot taken at <see cref="ForEdit" />. Always true for
    ///     Create/Open/Duplicate (no snapshot captured).
    /// </summary>
    private bool NeedsReachabilityCheck =>
        _reachabilitySnapshot is not { } snap ||
        !snap.Equals(CaptureReachabilitySnapshot());

    // --- Commands: browsing and source dirs ---

    [RelayCommand]
    private async Task BrowseSourceDirectories()
    {
        if (_filePicker is not { } filePicker) return;
        var paths = await filePicker.PickFoldersAsync(Strings.Get("Picker.SelectSourceDirs"));
        foreach (var path in paths)
            if (!SourceDirectories.Contains(path))
                SourceDirectories.Add(path);
    }

    [RelayCommand]
    private void RemoveSourceDirectory(string path)
    {
        SourceDirectories.Remove(path);
    }

    [RelayCommand]
    private async Task BrowseRepoPath()
    {
        if (_filePicker is not { } filePicker) return;
        var path = await filePicker.PickFolderAsync(Strings.Get("Picker.SelectRepository"));
        if (path is not null)
            RepoPath = path;
    }

    [RelayCommand]
    private async Task BrowseSshKey()
    {
        if (_filePicker is not { } filePicker) return;
        var path = await filePicker.PickFileAsync(Strings.Get("Picker.SelectSshKey"));
        if (path is not null)
            SshKeyPath = path;
    }

    // --- Factories ---

    /// <summary>Creates a VM for adding a new repository (Create button + borg init).</summary>
    public static RepositoryEditorViewModel ForNew(
        IFilePickerService filePicker,
        IRepositoryVerificationOperationRunner? verificationRunner = null,
        bool isBorg2SupportEnabled = false)
    {
        return new RepositoryEditorViewModel(filePicker, verificationRunner, isBorg2SupportEnabled)
        {
            IsNew = true
        };
    }

    /// <summary>Creates a VM for opening an existing borg repo (Open button + borg info).</summary>
    public static RepositoryEditorViewModel ForOpen(
        IFilePickerService filePicker,
        IRepositoryVerificationOperationRunner? verificationRunner = null,
        bool isBorg2SupportEnabled = false)
    {
        return new RepositoryEditorViewModel(filePicker, verificationRunner, isBorg2SupportEnabled)
        {
            IsOpen = true
        };
    }

    /// <summary>
    ///     Creates a VM for editing an existing repository. The VM loads the repo's
    ///     current state into its own edit fields; the live <paramref name="repo" />
    ///     is held as the target and is only mutated by <see cref="Save" /> after
    ///     verification succeeds.
    /// </summary>
    public static RepositoryEditorViewModel ForEdit(
        IFilePickerService filePicker,
        RepositoryViewModel repo,
        IRepositoryVerificationOperationRunner? verificationRunner = null,
        bool isBorg2SupportEnabled = false)
    {
        var vm = new RepositoryEditorViewModel(filePicker, verificationRunner, isBorg2SupportEnabled);
        vm.LoadFrom(repo);
        vm._targetRepo = repo;
        vm._reachabilitySnapshot = vm.CaptureReachabilitySnapshot();
        return vm;
    }

    /// <summary>
    ///     Creates a VM seeded with a copy of <paramref name="source" />'s persisted state,
    ///     with <see cref="RepositoryEditorViewModel.Name" /> overridden to <paramref name="newName" />.
    /// </summary>
    public static RepositoryEditorViewModel ForDuplicate(
        IFilePickerService filePicker,
        RepositoryViewModel source,
        string newName,
        IRepositoryVerificationOperationRunner? verificationRunner = null,
        bool isBorg2SupportEnabled = false)
    {
        var vm = new RepositoryEditorViewModel(filePicker, verificationRunner, isBorg2SupportEnabled);
        vm.LoadFrom(source);
        vm.Name = newName;
        if (!isBorg2SupportEnabled)
            vm.BorgVersion = BorgVersion.Borg1;
        vm.IsDuplicate = true;
        return vm;
    }

    /// <summary>Copies <paramref name="repo" />'s persistable fields into this VM.</summary>
    private void LoadFrom(RepositoryViewModel repo)
    {
        LoadFrom(repo.Model);
    }

    private void LoadFrom(BorgRepository repo)
    {
        Name = repo.Name;
        ArchiveNamePrefix = repo.ArchiveNamePrefix;
        var locationKind = repo.LocationKind == BorgRepositoryLocationKind.LocalPath && !repo.IsLocal
            ? BorgRepositoryLocationKind.SshFields
            : repo.LocationKind;
        IsBorgUrlLocation = locationKind == BorgRepositoryLocationKind.BorgUrl;
        IsLocal = locationKind == BorgRepositoryLocationKind.LocalPath;
        BorgVersion = repo.BorgVersion;
        EncryptionMode = repo.EncryptionMode;
        Borg2EncryptionMode = repo.Borg2EncryptionMode;
        BorgUrl = locationKind == BorgRepositoryLocationKind.BorgUrl
            ? string.IsNullOrWhiteSpace(repo.BorgUrl) ? repo.Path : repo.BorgUrl
            : string.Empty;
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

        _vmLastBackupAt = repo.LastBackupAt;

        if (locationKind == BorgRepositoryLocationKind.BorgUrl)
            RepoPath = string.Empty;
        else
            DecomposePath(repo.Path);
    }

    /// <summary>Writes current VM fields onto <paramref name="repo" /> (schedule + source dirs + config).</summary>
    private void ApplyTo(BorgRepository repo)
    {
        repo.Name = Name;
        repo.ArchiveNamePrefix = ArchiveNamePrefix;
        repo.LocationKind = CurrentLocationKind;
        repo.IsLocal = repo.LocationKind == BorgRepositoryLocationKind.LocalPath;
        repo.BorgUrl = repo.LocationKind == BorgRepositoryLocationKind.BorgUrl ? BorgUrl.Trim() : string.Empty;
        repo.Path = ComposePath();
        repo.BorgVersion = BorgVersion;
        repo.EncryptionMode = EncryptionMode;
        repo.Borg2EncryptionMode = Borg2EncryptionMode;
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

        repo.LastBackupAt = _vmLastBackupAt;

        if (!repo.Schedule.RunMissed && RunMissed) repo.LastBackupAt = DateTime.Now;

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

    /// <summary>
    ///     Builds the full repo path from <see cref="RepositoryEditorViewModel.IsLocal" />, host, user, and
    ///     <see cref="RepositoryEditorViewModel.RepoPath" />.
    /// </summary>
    internal string ComposePath()
    {
        if (IsBorgUrlLocation) return BorgUrl.Trim();
        if (IsLocal) return RepoPath;
        var userPart = string.IsNullOrWhiteSpace(SshUser) ? "" : $"{SshUser.Trim()}@";
        return $"{userPart}{SshHost.Trim()}:{RepoPath.Trim()}";
    }

    private BorgRepositoryLocationKind CurrentLocationKind => IsBorgUrlLocation
        ? BorgRepositoryLocationKind.BorgUrl
        : IsLocal
            ? BorgRepositoryLocationKind.LocalPath
            : BorgRepositoryLocationKind.SshFields;

    // --- Reachability check (Edit mode: skip borg info when nothing relevant changed) ---

    private ReachabilitySnapshot CaptureReachabilitySnapshot()
    {
        return new ReachabilitySnapshot(
            IsLocal, IsBorgUrlLocation, BorgUrl, SshHost, SshUser, SshPort, SshKeyPath, RepoPath,
            BorgVersion, EncryptionMode, Borg2EncryptionMode, BorgRemotePath);
    }

    // --- Diagnostic buttons (Check Version / Check Remote Path) ---

    [RelayCommand]
    private async Task CheckVersion()
    {
        if (_verificationRunner is not { } verificationRunner) return;
        VerifyError = null;
        VerifyStatus = Strings.Get("Status.CheckingVersion");
        var result = await verificationRunner.DetectVersionAsync(
            new VersionDetectionOperationOptions(
                new BorgRepository { BorgVersion = BorgVersion },
                NullOperationProgressSink.Instance));
        if (result.IsT0)
        {
            VerifyStatus = result.AsT0.VersionText;
        }
        else
        {
            VerifyStatus = null;
            VerifyError = result.AsT1.ToDisplayMessage();
        }
    }

    [RelayCommand]
    private async Task CheckRemotePath()
    {
        if (string.IsNullOrWhiteSpace(BorgRemotePath)) return;
        if (_verificationRunner is not { } verificationRunner) return;

        var userPart = string.IsNullOrWhiteSpace(SshUser) ? "" : $"{SshUser.Trim()}@";
        var host = $"{userPart}{SshHost.Trim()}";
        VerifyError = null;
        VerifyStatus = Strings.Get("Status.CheckingRemotePath");
        var result = await verificationRunner.CheckRemotePathAsync(
            new RemotePathCheckOperationOptions(
                new BorgRepository { BorgVersion = BorgVersion },
                NullOperationProgressSink.Instance,
                host,
                BorgRemotePath,
                SshKeyPath));
        if (result.IsT0)
        {
            VerifyStatus = Strings.Get("Status.RemotePathOk");
        }
        else
        {
            VerifyStatus = null;
            VerifyError = result.AsT1.ToDisplayMessage();
        }
    }

    // --- Save ---

    private bool CanSave()
    {
        return !IsVerifying;
    }

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
        if (_verificationRunner is null)
        {
            CommitResult(candidate);
            return;
        }

        // Edit mode: if no reachability-affecting field changed, the existing repo
        // is still reachable with the same credentials — skip the borg info call.
        if (_targetRepo is not null && !NeedsReachabilityCheck)
        {
            CommitResult(candidate);
            return;
        }

        VerifyError = null;
        IsVerifying = true;
        try
        {
            if (!await PrepareSshKeyIfNeededAsync(candidate))
                return;

            if (IsNew || IsDuplicate)
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

        if (IsBorgUrlLocation)
        {
            if (BorgVersion != BorgVersion.Borg2)
            {
                PathError = Strings.Get("Validation.BorgUrlRequiresBorg2");
                valid = false;
            }
            else if (string.IsNullOrWhiteSpace(BorgUrl))
            {
                PathError = Strings.Get("ValidationRequired");
                valid = false;
            }
            else if (!BorgRepositoryUrlValidator.IsSupportedBorg2Url(BorgUrl))
            {
                PathError = Strings.Get(BorgRepositoryUrlValidator.HasEmbeddedSecret(BorgUrl)
                    ? "Validation.BorgUrlCredentials"
                    : "Validation.BorgUrlUnsupported");
                valid = false;
            }
        }
        else if (string.IsNullOrWhiteSpace(RepoPath))
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

    private async Task<bool> PrepareSshKeyIfNeededAsync(BorgRepository candidate)
    {
        if (_verificationRunner is null || !ShowSshKeyOptions || string.IsNullOrWhiteSpace(SshKeyPath))
            return true;

        var result = await _verificationRunner.PrepareSshKeyAsync(
            new SshKeyPreparationOperationOptions(
                candidate,
                NullOperationProgressSink.Instance,
                SshKeyPath));
        if (result.IsT0)
            return true;

        if (result.AsT1 is OperationError.Cancelled)
            return false;

        VerifyStatus = null;
        VerifyError = result.AsT1.ToDisplayMessage();
        return false;
    }

    /// <summary>
    ///     Commits a successful save. For Edit: applies current VM state to the live
    ///     target repo (in place, so caller references stay valid). For Create/Open:
    ///     exposes the candidate as the result for the caller to add to the list.
    ///     Then flips IsSaved, which auto-closes the dialog via ModalWindow's ISaveable hook.
    /// </summary>
    private void CommitResult(BorgRepository candidate)
    {
        if (_targetRepo is not null)
        {
            ApplyTo(_targetRepo.Model);
            _targetRepo.RefreshPersistedProperties();
            Repository = _targetRepo;
        }
        else
        {
            Repository = new RepositoryViewModel(candidate);
        }

        IsSaved = true;
    }

    /// <summary>
    ///     Initializes a new borg repository on <paramref name="candidate" />. On success,
    ///     commits the result. On "already exists", prompts the user to open it instead.
    ///     On any other failure, sets VerifyError and leaves the dialog open.
    /// </summary>
    private async Task VerifyByInitializingAsync(BorgRepository candidate)
    {
        if (_verificationRunner is not { } verificationRunner) return;

        VerifyStatus = Strings.Get("Status.InitializingRepo");
        var result = await verificationRunner.VerifyNewRepositoryAsync(
            new RepositoryVerificationOperationOptions(
                candidate,
                NullOperationProgressSink.Instance,
                true));

        if (result.IsT0)
        {
            CommitResult(candidate);
            return;
        }

        if (result.AsT1 is OperationError.BorgFailure { Code: BorgModernExitCode.RepositoryAlreadyExists })
        {
            if (await DialogHelper.ConfirmAsync(Strings.Get("RepoExistsOpenInstead")))
            {
                CommitResult(candidate);
                return;
            }

            VerifyError = BorgErrorMessages.GetMessage(BorgModernExitCode.RepositoryAlreadyExists);
            return;
        }

        if (result.AsT1 is OperationError.Cancelled)
            return;

        VerifyError = result.AsT1.ToDisplayMessage();
    }

    /// <summary>
    ///     Verifies <paramref name="candidate" /> by running `borg info`, which proves
    ///     both reachability and credential validity. Commits the result on success;
    ///     sets VerifyError and keeps the dialog open on failure.
    /// </summary>
    private async Task VerifyByInfoAsync(BorgRepository candidate)
    {
        if (_verificationRunner is not { } verificationRunner) return;

        VerifyStatus = Strings.Get("Status.VerifyingRepo");
        var result = await verificationRunner.VerifyExistingRepositoryAsync(
            new RepositoryVerificationOperationOptions(
                candidate,
                NullOperationProgressSink.Instance,
                false));

        if (result.IsT0)
        {
            CommitResult(candidate);
            return;
        }

        if (result.AsT1 is OperationError.Cancelled)
            return;

        VerifyError = result.AsT1.ToDisplayMessage();
    }

    private readonly record struct ReachabilitySnapshot(
        bool IsLocal,
        bool IsBorgUrlLocation,
        string BorgUrl,
        string SshHost,
        string SshUser,
        int SshPort,
        string SshKeyPath,
        string RepoPath,
        BorgVersion BorgVersion,
        BorgEncryptionMode EncryptionMode,
        Borg2EncryptionMode Borg2EncryptionMode,
        string BorgRemotePath);
}
