using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BorgMate.Models;

public partial class BorgRepository : ObservableObject
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _path = string.Empty;

    [ObservableProperty]
    private BorgEncryptionMode _encryptionMode = BorgEncryptionMode.RepokeyBlake2;

    [ObservableProperty]
    private string _sshKeyPath = string.Empty;

    [ObservableProperty]
    private string _passphrase = string.Empty;

    /// <summary>Upload rate limit in KB/s. Maps to borg's --upload-ratelimit. 0 = unlimited.</summary>
    [ObservableProperty]
    private int _rateLimit;

    [ObservableProperty]
    private int _sshPort = 22;

    [ObservableProperty]
    private BorgVersion _borgVersion = BorgVersion.Borg1;

    [ObservableProperty]
    private string _borgRemotePath = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSsh))]
    private bool _isLocal = true;

    public bool IsSsh => !IsLocal;

    /// <summary>
    /// True when any operation (backup, restore, list, delete) is running on this repo.
    /// </summary>
    [ObservableProperty]
    private bool _isBusy;

    /// <summary>Progress percentage (0–100) of the active command job, or null for indeterminate.</summary>
    [ObservableProperty]
    private double? _commandProgress;

    /// <summary>
    /// Set when passphrase retries are exhausted. Forces re-prompt on next interaction.
    /// In-memory only, not persisted.
    /// </summary>
    [ObservableProperty]
    private bool _wrongPassphrase;

    /// <summary>
    /// True after the last operation (command or query) failed. Cleared after any
    /// successful operation. In-memory only, not persisted — resets on app restart.
    /// </summary>
    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    private string? _lastError;

    /// <summary>
    /// SSH key passphrase for agent loading. In-memory only, not persisted.
    /// On WSL, stored on repo for inline SSH_ASKPASS use (no ssh-agent required).
    /// </summary>
    public string? SshKeyPassphrase { get; set; }

    // Backup configuration (moved from BackupTask)
    public ObservableCollection<string> SourceDirectories { get; } = [];

    [ObservableProperty]
    private BackupMode _mode = BackupMode.Manual;

    [ObservableProperty]
    private BackupSchedule _schedule = new();

    /// <summary>Last successful backup timestamp. Persisted to config.</summary>
    [ObservableProperty]
    private DateTime? _lastBackupAt;

    public bool IsScheduled => Mode == BackupMode.Scheduled && SourceDirectories.Count > 0;

    /// <summary>Fires PropertyChanged to trigger converter re-evaluation for schedule display.</summary>
    public void RefreshScheduleDisplay()
    {
        OnPropertyChanged(nameof(IsScheduled));
        OnPropertyChanged(nameof(LastBackupAt));
    }

    // --- Loaded archive list (from last successful borg list, null = never fetched) ---

    public List<BorgArchive>? LoadedArchives { get; set; }

    // --- Repository stats (raw values from borg info --json) ---

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStats))]
    private long? _statsTotalSize;

    [ObservableProperty]
    private long? _statsTotalChunks;

    [ObservableProperty]
    private bool _isLoadingStats;

    public bool HasStats => StatsTotalSize is not null;

    /// <summary>Forces re-read of stats properties (triggers converter re-evaluation on language change).</summary>
    public void RefreshStats()
    {
        OnPropertyChanged(nameof(StatsTotalSize));
        OnPropertyChanged(nameof(StatsTotalChunks));
    }
}
