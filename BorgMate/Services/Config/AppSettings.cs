using System.Text.Json.Serialization;
using BorgMate.Models;

namespace BorgMate.Services.Config;

public class AppSettings
{
    [JsonIgnore]
    public string DefaultBorgPath { get; } = BorgPathHelper.Detect();

    public string? BorgBinaryPath { get; set; }
    public AppTheme Theme { get; set; } = AppTheme.Auto;
    public bool CheckForUpdates { get; set; } = true;
    public bool ShowNotifications { get; set; } = true;
    public bool StartAtLogin { get; set; }
    public bool StartMinimized { get; set; }
    public string Language { get; set; } = "auto";
    public bool LoggingEnabled { get; set; } = true;
    public AppLogLevel LogLevel { get; set; } = AppLogLevel.Info;
    public RetentionPeriod? LogRetention { get; set; } = RetentionPeriod.OneWeek;
    public bool SidebarExpanded { get; set; }
    public RetentionPeriod? JournalRetention { get; set; } = RetentionPeriod.OneWeek;
    public double? WindowX { get; set; }
    public double? WindowY { get; set; }
    public double? WindowWidth { get; set; }
    public double? WindowHeight { get; set; }
    public bool IsMaximized { get; set; }
    /// <summary>True = binary units (KiB/MiB/GiB, base 1024). False = decimal units (KB/MB/GB, base 1000).</summary>
    public bool BinaryUnits { get; set; } = true;

    /// <summary>SSH ServerAliveInterval in seconds. 0 disables keep-alive.</summary>
    public int SshKeepAliveInterval { get; set; } = 30;

    /// <summary>SSH ServerAliveCountMax — number of missed keep-alive responses before disconnect.</summary>
    public int SshKeepAliveCountMax { get; set; } = 3;

    /// <summary>Auto-fetch the archive list when a repository is selected.</summary>
    public bool AutoLoadArchives { get; set; } = true;

    /// <summary>Auto-fetch repository statistics when a repository is selected.</summary>
    public bool AutoLoadStats { get; set; } = true;

    /// <summary>Auto-fetch per-archive details on archive selection.</summary>
    public bool AutoLoadArchiveDetails { get; set; } = true;

    /// <summary>Returns effective borg binary path (user override or auto-detected).</summary>
    [JsonIgnore]
    public string EffectiveBorgPath =>
        !string.IsNullOrWhiteSpace(BorgBinaryPath) && BorgBinaryPath != "borg"
            ? BorgBinaryPath
            : DefaultBorgPath;
}
