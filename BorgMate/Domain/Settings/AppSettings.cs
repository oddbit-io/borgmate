using System;
using System.Text.Json.Serialization;
using BorgMate.Domain.Repositories;
using BorgMate.Infrastructure.Borg;

namespace BorgMate.Domain.Settings;

public class AppSettings
{
    private readonly Lazy<BorgPathDetectionResult> _borg1PathDetection;
    private readonly Lazy<BorgPathDetectionResult> _borg2PathDetection;

    /// <summary>Initializes app settings. Borg executable defaults are detected lazily.</summary>
    public AppSettings()
        : this(
            () => BorgPathHelper.DetectWithStatus(BorgVersion.Borg1),
            () => BorgPathHelper.DetectWithStatus(BorgVersion.Borg2))
    {
    }

    internal AppSettings(BorgPathDetectionResult borg1PathDetection, BorgPathDetectionResult borg2PathDetection)
        : this(() => borg1PathDetection, () => borg2PathDetection)
    {
    }

    internal AppSettings(
        Func<BorgPathDetectionResult> borg1PathDetection,
        Func<BorgPathDetectionResult> borg2PathDetection)
    {
        _borg1PathDetection = new Lazy<BorgPathDetectionResult>(borg1PathDetection);
        _borg2PathDetection = new Lazy<BorgPathDetectionResult>(borg2PathDetection);
    }

    /// <summary>Legacy Borg binary default accessor, mapped to the Borg 1 default.</summary>
    [JsonIgnore]
    public string DefaultBorgPath => DefaultBorg1Path;

    /// <summary>Effective default Borg 1 binary path, using a conventional command name when detection fails.</summary>
    [JsonIgnore]
    public string DefaultBorg1Path => _borg1PathDetection.Value.EffectivePath;

    /// <summary>Verified Borg 1 binary path, or null when no Borg 1 executable was detected.</summary>
    [JsonIgnore]
    public string? DetectedBorg1Path => _borg1PathDetection.Value.DetectedPath;

    /// <summary>Effective default Borg 2 binary path, using a conventional command name when detection fails.</summary>
    [JsonIgnore]
    public string DefaultBorg2Path => _borg2PathDetection.Value.EffectivePath;

    /// <summary>Verified Borg 2 binary path, or null when no Borg 2 executable was detected.</summary>
    [JsonIgnore]
    public string? DetectedBorg2Path => _borg2PathDetection.Value.DetectedPath;

    /// <summary>Legacy Borg binary override kept for config migration.</summary>
    public string? BorgBinaryPath { get; set; }

    /// <summary>Optional user override for Borg 1.x command execution.</summary>
    public string? Borg1BinaryPath { get; set; }

    /// <summary>Optional user override for Borg 2.x command execution.</summary>
    public string? Borg2BinaryPath { get; set; }

    /// <summary>Enables experimental Borg 2 repository options in the UI.</summary>
    public bool ExperimentalBorg2Support { get; set; }

    public AppTheme Theme { get; set; } = AppTheme.Auto;
    public bool CheckForUpdates { get; set; } = true;
    public bool ShowNotifications { get; set; } = true;
    public bool StartAtLogin { get; set; }
    public bool StartMinimized { get; set; }
    public bool WakeForScheduledBackups { get; set; }
    public bool ScheduledBackupsOnlyOnAcPower { get; set; } = true;
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

    /// <summary>Default options shown when restoring archives.</summary>
    public BorgExtractOptions DefaultExtractOptions { get; set; } = new();

    /// <summary>Returns effective borg binary path (user override or auto-detected).</summary>
    [JsonIgnore]
    public string EffectiveBorgPath => GetEffectiveBorgPath(BorgVersion.Borg1);

    /// <summary>Returns the effective borg binary path for a Borg CLI generation.</summary>
    public string GetEffectiveBorgPath(BorgVersion borgVersion)
    {
        var configured = borgVersion == BorgVersion.Borg2 ? Borg2BinaryPath : Borg1BinaryPath;
        var fallback = borgVersion == BorgVersion.Borg2 ? DefaultBorg2Path : DefaultBorg1Path;
        return !string.IsNullOrWhiteSpace(configured)
            ? configured
            : fallback;
    }
}
