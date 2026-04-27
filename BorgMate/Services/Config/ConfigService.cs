using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using BorgMate.Models;
using Microsoft.Extensions.Logging;

namespace BorgMate.Services.Config;

public class ConfigService(ILogger<ConfigService>? logger = null) : IConfigService
{
    public event Action? SaveRequested;

    public void RequestSave() => SaveRequested?.Invoke();

    private static string ConfigDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "BorgMate");

    private static string ConfigFilePath =>
        Path.Combine(ConfigDirectory, "config.json");

    public ConfigData Load()
    {
        try
        {
            if (!File.Exists(ConfigFilePath))
                return new ConfigData();

            var json = File.ReadAllText(ConfigFilePath);
            return JsonSerializer.Deserialize(json, ConfigJsonContext.Default.ConfigData) ?? new ConfigData();
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to load config from {Path}", ConfigFilePath);
            return new ConfigData();
        }
    }

    public void Save(ConfigData data)
    {
        try
        {
            Directory.CreateDirectory(ConfigDirectory);
            var json = JsonSerializer.Serialize(data, ConfigJsonContext.Default.ConfigData);

            // Skip write if content hasn't changed
            if (File.Exists(ConfigFilePath) && File.ReadAllText(ConfigFilePath) == json)
                return;

            // Atomic write: write to temp file then rename, so a crash mid-write
            // can't corrupt the config. File.Move with overwrite is atomic on all
            // platforms when source and destination are on the same filesystem.
            var tmpPath = ConfigFilePath + ".tmp";
            File.WriteAllText(tmpPath, json);
            File.Move(tmpPath, ConfigFilePath, overwrite: true);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to save config to {Path}", ConfigFilePath);
        }
    }

    public static BorgRepository ToModel(RepositoryData data)
    {
        var repo = new BorgRepository
        {
            Name = data.Name,
            Path = data.Path,
            EncryptionMode = data.EncryptionMode,
            SshKeyPath = data.SshKeyPath,
            RateLimit = data.RateLimit,
            SshPort = data.SshPort,
            BorgVersion = data.BorgVersion,
            BorgRemotePath = data.BorgRemotePath,
            IsLocal = data.IsLocal,
            Mode = data.Mode ?? BackupMode.Manual
        };
        if (data.SourceDirectories is not null)
            foreach (var dir in data.SourceDirectories)
                repo.SourceDirectories.Add(dir);
        if (data.Schedule is not null)
        {
            repo.Schedule.Frequency = data.Schedule.Frequency;
            repo.Schedule.Hour = data.Schedule.Hour;
            repo.Schedule.Minute = data.Schedule.Minute;
            repo.Schedule.DayOfWeek = Enum.TryParse<DayOfWeek>(data.Schedule.DayOfWeek, out var dow) ? dow : DayOfWeek.Monday;
            repo.Schedule.DayOfMonth = data.Schedule.DayOfMonth;
            repo.Schedule.IntervalHours = data.Schedule.IntervalHours;
            repo.Schedule.RunMissed = data.Schedule.RunMissed;
            repo.Schedule.RunPruneAfterBackup = data.Schedule.RunPruneAfterBackup;
        }
        if (data.LastBackupAt is not null && DateTime.TryParse(data.LastBackupAt, out var lastBackup))
            repo.LastBackupAt = lastBackup;
        if (data.PruneOptions is not null)
        {
            repo.PruneOptions.KeepLast = data.PruneOptions.KeepLast;
            repo.PruneOptions.KeepHourly = data.PruneOptions.KeepHourly;
            repo.PruneOptions.KeepDaily = data.PruneOptions.KeepDaily;
            repo.PruneOptions.KeepWeekly = data.PruneOptions.KeepWeekly;
            repo.PruneOptions.KeepMonthly = data.PruneOptions.KeepMonthly;
            repo.PruneOptions.KeepYearly = data.PruneOptions.KeepYearly;
            repo.PruneOptions.CompactAfterPrune = data.PruneOptions.CompactAfterPrune;
        }
        return repo;
    }

    public static RepositoryData FromModel(BorgRepository repo)
    {
        return new RepositoryData
        {
            Name = repo.Name,
            Path = repo.Path,
            EncryptionMode = repo.EncryptionMode,
            SshKeyPath = repo.SshKeyPath,
            RateLimit = repo.RateLimit,
            SshPort = repo.SshPort,
            BorgVersion = repo.BorgVersion,
            BorgRemotePath = repo.BorgRemotePath,
            IsLocal = repo.IsLocal,
            SourceDirectories = repo.SourceDirectories.Count > 0 ? repo.SourceDirectories.ToList() : null,
            Mode = repo.Mode != BackupMode.Manual ? repo.Mode : null,
            Schedule = repo.Mode != BackupMode.Manual ? new BackupScheduleData
            {
                Frequency = repo.Schedule.Frequency,
                Hour = repo.Schedule.Hour,
                Minute = repo.Schedule.Minute,
                DayOfWeek = repo.Schedule.DayOfWeek.ToString(),
                DayOfMonth = repo.Schedule.DayOfMonth,
                IntervalHours = repo.Schedule.IntervalHours,
                RunMissed = repo.Schedule.RunMissed,
                RunPruneAfterBackup = repo.Schedule.RunPruneAfterBackup
            } : null,
            LastBackupAt = repo.LastBackupAt?.ToString("o"),
            PruneOptions = repo.PruneOptions.HasAnyRetention ? new PruneOptionsData
            {
                KeepLast = repo.PruneOptions.KeepLast,
                KeepHourly = repo.PruneOptions.KeepHourly,
                KeepDaily = repo.PruneOptions.KeepDaily,
                KeepWeekly = repo.PruneOptions.KeepWeekly,
                KeepMonthly = repo.PruneOptions.KeepMonthly,
                KeepYearly = repo.PruneOptions.KeepYearly,
                CompactAfterPrune = repo.PruneOptions.CompactAfterPrune
            } : null
        };
    }

}
