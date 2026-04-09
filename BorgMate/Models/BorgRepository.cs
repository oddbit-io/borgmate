using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Humanizer;

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

    /// <summary>
    /// Set when passphrase retries are exhausted. Forces re-prompt on next interaction.
    /// In-memory only, not persisted.
    /// </summary>
    [ObservableProperty]
    private bool _wrongPassphrase;

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

    public string ScheduleDisplay
    {
        get
        {
            if (!IsScheduled) return Localization.Strings.Get("Mode.Manual");
            var nextRun = Services.SchedulerService.ComputeNextRun(this);
            var next = HumanizeNextRun(nextRun);
            return $"{Schedule.ScheduleDisplay} · {next}";
        }
    }

    public string? ScheduleTooltip
    {
        get
        {
            if (!IsScheduled) return null;
            var nextRun = Services.SchedulerService.ComputeNextRun(this);
            var next = nextRun?.ToString("g", Localization.Strings.Culture) ?? "—";
            return $"{Schedule.ScheduleDisplay}\n{Localization.Strings.Get("Schedule.NextRun")}: {next}";
        }
    }

    private static string HumanizeNextRun(DateTime? nextRun)
    {
        if (nextRun is null) return "—";
        var remaining = nextRun.Value - DateTime.Now;
        if (remaining <= TimeSpan.Zero) return Localization.Strings.Get("Schedule.Now");
        var text = remaining.Humanize(precision: 2, minUnit: TimeUnit.Minute, culture: Localization.Strings.Culture);
        return string.Format(Localization.Strings.Get("Schedule.InTime"), text);
    }

    public void RefreshScheduleDisplay()
    {
        OnPropertyChanged(nameof(ScheduleDisplay));
        OnPropertyChanged(nameof(ScheduleTooltip));
        OnPropertyChanged(nameof(IsScheduled));
    }
}
