using System.Collections.ObjectModel;
using System.Threading.Tasks;
using BorgMate.Models;
using BorgMate.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BorgMate.ViewModels;

public partial class RepositoryEditorViewModel : ViewModelBase, ISaveable
{
    private readonly BorgServiceFactory? _borgServiceFactory;
    private readonly IFilePickerService? _filePicker;

    public RepositoryEditorViewModel() { }

    public RepositoryEditorViewModel(BorgServiceFactory borgServiceFactory, IFilePickerService filePicker)
    {
        _borgServiceFactory = borgServiceFactory;
        _filePicker = filePicker;
    }

    // Source directories
    public ObservableCollection<string> SourceDirectories { get; } = [];

    [ObservableProperty]
    private string? _selectedDirectory;

    [ObservableProperty]
    private BorgRepository _repository = new() { IsLocal = true };

    // Decomposed SSH fields for the editor UI
    [ObservableProperty]
    private string _sshHost = string.Empty;

    [ObservableProperty]
    private string _sshUser = string.Empty;

    [ObservableProperty]
    private string _repoPath = string.Empty;

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

    public string Title => IsNew ? Strings.Get("CreateNewRepo")
        : IsOpen ? Strings.Get("OpenExistingRepo")
        : Strings.Get("EditRepo");

    public string SaveButtonText => IsNew ? Strings.Get("Create")
        : IsOpen ? Strings.Get("Open")
        : Strings.Get("Save");

    public BorgVersion[] BorgVersions { get; } = System.Enum.GetValues<BorgVersion>();

    public BorgEncryptionMode[] BorgEncryptionModes { get; } = System.Enum.GetValues<BorgEncryptionMode>();

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

    // Schedule fields
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

    /// <summary>Creates a VM for adding a new repository (shows init tab, Create button).</summary>
    public static RepositoryEditorViewModel ForNew(BorgServiceFactory factory, IFilePickerService filePicker)
    {
        return new RepositoryEditorViewModel(factory, filePicker)
        {
            IsNew = true,
            Repository = new BorgRepository
            {
                BorgVersion = BorgVersion.Borg1,
                EncryptionMode = BorgEncryptionMode.RepokeyBlake2,
                IsLocal = true
            }
        };
    }

    /// <summary>Creates a VM for opening an existing borg repo (no init, Open button).</summary>
    public static RepositoryEditorViewModel ForOpen(BorgServiceFactory factory, IFilePickerService filePicker)
    {
        return new RepositoryEditorViewModel(factory, filePicker)
        {
            IsNew = false,
            IsOpen = true,
            Repository = new BorgRepository
            {
                BorgVersion = BorgVersion.Borg1,
                EncryptionMode = BorgEncryptionMode.RepokeyBlake2,
                IsLocal = true
            }
        };
    }

    /// <summary>Creates a VM for editing an existing repository (pre-populates all fields, Save button).</summary>
    public static RepositoryEditorViewModel ForEdit(BorgServiceFactory factory, IFilePickerService filePicker, BorgRepository repo)
    {
        var vm = new RepositoryEditorViewModel(factory, filePicker)
        {
            IsNew = false,
            Repository = repo,
            Mode = repo.Mode,
            SelectedFrequency = repo.Schedule.Frequency,
            ScheduleHour = repo.Schedule.Hour,
            ScheduleMinute = repo.Schedule.Minute,
            SelectedDayOfWeek = repo.Schedule.DayOfWeek,
            ScheduleDayOfMonth = repo.Schedule.DayOfMonth,
            IntervalHours = repo.Schedule.IntervalHours,
            RunMissed = repo.Schedule.RunMissed
        };
        foreach (var dir in repo.SourceDirectories)
            vm.SourceDirectories.Add(dir);
        vm.DecomposePath();
        return vm;
    }

    /// <summary>Splits user@host:/path into separate fields.</summary>
    private void DecomposePath()
    {
        if (Repository.IsLocal)
        {
            RepoPath = Repository.Path;
            return;
        }

        var path = Repository.Path;
        // Format: user@host:/path or host:/path
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

    /// <summary>Composes user@host:/path from separate fields.</summary>
    private void ComposePath()
    {
        if (Repository.IsLocal)
        {
            Repository.Path = RepoPath;
            return;
        }

        var userPart = string.IsNullOrWhiteSpace(SshUser) ? "" : $"{SshUser.Trim()}@";
        Repository.Path = $"{userPart}{SshHost.Trim()}:{RepoPath.Trim()}";
    }

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
            Repository.SshKeyPath = path;
    }

    [RelayCommand]
    private async Task CheckVersion()
    {
        var service = _borgServiceFactory!.GetService(Repository.BorgVersion);
        await service.GetVersionAsync();
        // TODO: surface result inline in the editor (commit 3).
    }

    [RelayCommand]
    private async Task CheckRemotePath()
    {
        if (string.IsNullOrWhiteSpace(Repository.BorgRemotePath)) return;

        var userPart = string.IsNullOrWhiteSpace(SshUser) ? "" : $"{SshUser.Trim()}@";
        var host = $"{userPart}{SshHost.Trim()}";
        var service = _borgServiceFactory!.GetService(Repository.BorgVersion);
        await service.CheckRemotePathAsync(
            host, Repository.BorgRemotePath, Repository.SshKeyPath);
        // TODO: surface result inline in the editor (commit 3).
    }

    [RelayCommand]
    private void Save()
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

        if (Repository.IsSsh && string.IsNullOrWhiteSpace(SshHost))
        {
            PathError = Strings.Get("ValidationRequired");
            valid = false;
        }

        if (Repository.IsSsh && string.IsNullOrWhiteSpace(Repository.SshKeyPath))
        {
            SshKeyError = Strings.Get("ValidationRequired");
            valid = false;
        }

        if (string.IsNullOrWhiteSpace(Repository.Name))
        {
            NameError = Strings.Get("ValidationRequired");
            valid = false;
        }

        if (!valid) return;

        ComposePath();

        // Apply source directories
        Repository.SourceDirectories.ReplaceWith(SourceDirectories);

        // Apply schedule
        Repository.Mode = Mode;
        Repository.Schedule.Frequency = SelectedFrequency;
        Repository.Schedule.Hour = ScheduleHour;
        Repository.Schedule.Minute = ScheduleMinute;
        Repository.Schedule.DayOfWeek = SelectedDayOfWeek;
        Repository.Schedule.DayOfMonth = ScheduleDayOfMonth;
        Repository.Schedule.IntervalHours = IntervalHours;
        Repository.Schedule.RunMissed = RunMissed;
        Repository.RefreshScheduleDisplay();

        IsSaved = true;
    }
}
