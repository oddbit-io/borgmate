using System.Collections.Generic;
using BorgMate.Models;

namespace BorgMate.Services.Config;

public class ConfigData
{
    public AppSettings Settings { get; set; } = new();
    public List<RepositoryData> Repositories { get; set; } = [];
    public List<BackupTaskData> Tasks { get; set; } = [];
}

public class RepositoryData
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public BorgEncryptionMode EncryptionMode { get; set; } = BorgEncryptionMode.RepokeyBlake2;
    public string SshKeyPath { get; set; } = string.Empty;
    public int RateLimit { get; set; }
    public int SshPort { get; set; } = 22;
    public BorgVersion BorgVersion { get; set; } = BorgVersion.Borg1;
    public string BorgRemotePath { get; set; } = string.Empty;
    public bool IsLocal { get; set; } = true;
    public List<string>? SourceDirectories { get; set; }
    public BackupScheduleData? Schedule { get; set; }
    public BackupMode? Mode { get; set; }
    public string? LastBackupAt { get; set; }
}

/// <summary>Legacy format. Migrated to BorgRepository source directories on first load.</summary>
public class BackupTaskData
{
    public string Name { get; set; } = string.Empty;
    public string RepositoryPath { get; set; } = string.Empty;
    public BackupMode Mode { get; set; } = BackupMode.Manual;
    public BackupScheduleData Schedule { get; set; } = new();
    public List<string> SourceDirectories { get; set; } = [];
}

public class BackupScheduleData
{
    public ScheduleFrequency Frequency { get; set; } = ScheduleFrequency.Daily;
    public int Hour { get; set; } = 2;
    public int Minute { get; set; }
    public string DayOfWeek { get; set; } = "Monday";
    public int DayOfMonth { get; set; } = 1;
    public int IntervalHours { get; set; } = 6;
    public bool RunMissed { get; set; } = true;
}
