using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BorgMate.Domain.Repositories;
using BorgMate.Infrastructure.Borg;
using Microsoft.Extensions.Logging;
using OneOf;

namespace BorgMate.Persistence.Configuration;

public class JsonAppConfigStore : ISettingsStore, IRepositoryConfigStore
{
    private readonly string _configDirectory;
    private readonly string _configFilePath;
    private readonly ILogger<JsonAppConfigStore>? _logger;
    private AppConfigDocument _data = new();
    private bool _loadedExistingConfig;
    private int _loadedRepositoryCount;
    private StoreError? _loadError;
    private bool _repositoryMutated;

    public JsonAppConfigStore(ILogger<JsonAppConfigStore>? logger = null)
        : this(DefaultConfigFilePath, logger)
    {
    }

    internal JsonAppConfigStore(string configFilePath, ILogger<JsonAppConfigStore>? logger = null)
    {
        _logger = logger;
        _configFilePath = configFilePath;
        _configDirectory = Path.GetDirectoryName(configFilePath) ?? string.Empty;
    }

    private static string DefaultConfigDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "BorgMate");

    private static string DefaultConfigFilePath =>
        Path.Combine(DefaultConfigDirectory, "config.json");

    event Action<RepositoryConfigSnapshot>? IRepositoryConfigStore.Changed
    {
        add => RepositoriesChanged += value;
        remove => RepositoriesChanged -= value;
    }

    RepositoryConfigSnapshot IRepositoryConfigStore.GetSnapshot()
    {
        return CreateRepositorySnapshot();
    }

    Task<OneOf<RepositoryConfigSnapshot, StoreError>> IRepositoryConfigStore.LoadAsync(CancellationToken ct)
    {
        return LoadRepositoriesAsync(ct);
    }

    Task<OneOf<StoreOk, StoreError>> IRepositoryConfigStore.SaveAsync(CancellationToken ct)
    {
        return SaveAsync(ct);
    }

    public async Task<OneOf<RepositoryConfigSnapshot, StoreError>> AddAsync(BorgRepository repo,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (_data.Repositories.Any(r => string.Equals(r.Path, repo.Path, StringComparison.Ordinal)))
            return new StoreError.Validation("Validation.RepositoryPathExists");

        _repositoryMutated = true;
        _data.Repositories.Add(FromModel(repo));
        var snapshot = CreateRepositorySnapshot();
        RepositoriesChanged?.Invoke(snapshot);
        await Task.CompletedTask;
        return snapshot;
    }

    public async Task<OneOf<RepositoryConfigSnapshot, StoreError>> UpdateAsync(BorgRepository repo,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var index = _data.Repositories.FindIndex(r => string.Equals(r.Path, repo.Path, StringComparison.Ordinal));

        if (index < 0)
            index = _data.Repositories.FindIndex(r => string.Equals(r.Name, repo.Name, StringComparison.Ordinal));

        _repositoryMutated = true;
        if (index < 0)
            _data.Repositories.Add(FromModel(repo));
        else
            _data.Repositories[index] = FromModel(repo);

        var snapshot = CreateRepositorySnapshot();
        RepositoriesChanged?.Invoke(snapshot);
        await Task.CompletedTask;
        return snapshot;
    }

    public async Task<OneOf<RepositoryConfigSnapshot, StoreError>> RemoveAsync(BorgRepository repo,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _repositoryMutated = true;
        _data.Repositories.RemoveAll(r => string.Equals(r.Path, repo.Path, StringComparison.Ordinal));
        var snapshot = CreateRepositorySnapshot();
        RepositoriesChanged?.Invoke(snapshot);
        await Task.CompletedTask;
        return snapshot;
    }

    public BorgRepository? FindByPath(string path)
    {
        return _data.Repositories
            .Where(r => string.Equals(r.Path, path, StringComparison.Ordinal))
            .Select(ToModel)
            .FirstOrDefault();
    }

    public string GenerateUniqueCopyName(string baseName)
    {
        var existing = new HashSet<string>(_data.Repositories.Select(r => r.Name), StringComparer.Ordinal);
        var first = string.Format(Strings.Get("Duplicate.NameCopy"), baseName);
        if (!existing.Contains(first)) return first;

        for (var i = 2;; i++)
        {
            var candidate = string.Format(Strings.Get("Duplicate.NameCopyN"), baseName, i);
            if (!existing.Contains(candidate)) return candidate;
        }
    }

    event Action<SettingsSnapshot>? ISettingsStore.Changed
    {
        add => SettingsChanged += value;
        remove => SettingsChanged -= value;
    }

    public void RequestSave()
    {
        _ = SaveAndNotifyAsync();
    }

    SettingsSnapshot ISettingsStore.GetSnapshot()
    {
        return GetSettingsSnapshot();
    }

    Task<OneOf<SettingsSnapshot, StoreError>> ISettingsStore.LoadAsync(CancellationToken ct)
    {
        return LoadSettingsAsync(ct);
    }

    Task<OneOf<StoreOk, StoreError>> ISettingsStore.SaveAsync(CancellationToken ct)
    {
        return SaveAsync(ct);
    }

    private event Action<SettingsSnapshot>? SettingsChanged;
    private event Action<RepositoryConfigSnapshot>? RepositoriesChanged;

    public SettingsSnapshot GetSettingsSnapshot()
    {
        return new SettingsSnapshot(_data.Settings);
    }

    public async Task<OneOf<SettingsSnapshot, StoreError>> LoadSettingsAsync(CancellationToken ct = default)
    {
        var result = await LoadCoreAsync(ct);
        return result.Match<OneOf<SettingsSnapshot, StoreError>>(
            _ => GetSettingsSnapshot(),
            error => error);
    }

    public async Task<OneOf<RepositoryConfigSnapshot, StoreError>> LoadRepositoriesAsync(CancellationToken ct = default)
    {
        var result = await LoadCoreAsync(ct);
        return result.Match<OneOf<RepositoryConfigSnapshot, StoreError>>(
            _ => CreateRepositorySnapshot(),
            error => error);
    }

    public Task<OneOf<StoreOk, StoreError>> SaveAsync(CancellationToken ct = default)
    {
        return SaveCoreAsync(ct);
    }

    public AppConfigDocument Load()
    {
        try
        {
            _data = LoadFromDisk();
            return _data;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to load config from {Path}", _configFilePath);
            _loadError = ex is JsonException
                ? new StoreError.Corrupt(ex.Message)
                : new StoreError.Io(ex);
            _data = new AppConfigDocument();
            return _data;
        }
    }

    private async Task<OneOf<StoreOk, StoreError>> LoadCoreAsync(CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            _data = await Task.Run(LoadFromDisk, ct);
            var settingsSnapshot = GetSettingsSnapshot();
            var repositorySnapshot = CreateRepositorySnapshot();
            SettingsChanged?.Invoke(settingsSnapshot);
            RepositoriesChanged?.Invoke(repositorySnapshot);
            return StoreOk.Instance;
        }
        catch (JsonException ex)
        {
            _logger?.LogError(ex, "Failed to parse config from {Path}", _configFilePath);
            _loadError = new StoreError.Corrupt(ex.Message);
            _data = new AppConfigDocument();
            return _loadError;
        }
        catch (IOException ex)
        {
            _logger?.LogError(ex, "Failed to load config from {Path}", _configFilePath);
            _loadError = new StoreError.Io(ex);
            _data = new AppConfigDocument();
            return _loadError;
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger?.LogError(ex, "Failed to load config from {Path}", _configFilePath);
            _loadError = new StoreError.Io(ex);
            _data = new AppConfigDocument();
            return _loadError;
        }
    }

    private AppConfigDocument LoadFromDisk()
    {
        _loadError = null;
        _repositoryMutated = false;
        _loadedExistingConfig = File.Exists(_configFilePath);
        _loadedRepositoryCount = 0;

        if (!_loadedExistingConfig)
            return new AppConfigDocument();

        var json = File.ReadAllText(_configFilePath);
        var data = JsonSerializer.Deserialize(json, AppConfigJsonContext.Default.AppConfigDocument) ??
                   new AppConfigDocument();
        NormalizeSettings(data);
        NormalizeRepositories(data);
        MigrateLegacyTasks(data);
        _loadedRepositoryCount = data.Repositories.Count;
        return data;
    }

    private async Task<OneOf<StoreOk, StoreError>> SaveCoreAsync(CancellationToken ct = default)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            if (_loadError is not null)
            {
                _logger?.LogError("Refusing to save config because loading {Path} failed", _configFilePath);
                return _loadError;
            }

            if (_loadedExistingConfig &&
                _loadedRepositoryCount > 0 &&
                _data.Repositories.Count == 0 &&
                !_repositoryMutated)
            {
                _logger?.LogError(
                    "Refusing to replace config {Path}: loaded {Count} repositories but current state is empty without explicit repository mutation",
                    _configFilePath,
                    _loadedRepositoryCount);
                return new StoreError.Validation("Config.RefusingEmptyRepositoryOverwrite");
            }

            await Task.Run(() =>
            {
                Directory.CreateDirectory(_configDirectory);
                var json = JsonSerializer.Serialize(_data, AppConfigJsonContext.Default.AppConfigDocument);

                // Skip write if content hasn't changed
                if (File.Exists(_configFilePath) && File.ReadAllText(_configFilePath) == json)
                    return;

                BackupExistingConfig();

                var tmpPath = _configFilePath + ".tmp";
                File.WriteAllText(tmpPath, json);
                File.Move(tmpPath, _configFilePath, true);
            }, ct);
            return StoreOk.Instance;
        }
        catch (IOException ex)
        {
            _logger?.LogError(ex, "Failed to save config to {Path}", _configFilePath);
            return new StoreError.Io(ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger?.LogError(ex, "Failed to save config to {Path}", _configFilePath);
            return new StoreError.Io(ex);
        }
    }

    private async Task SaveAndNotifyAsync()
    {
        var result = await SaveCoreAsync();
        if (result.IsT0)
        {
            SettingsChanged?.Invoke(GetSettingsSnapshot());
            RepositoriesChanged?.Invoke(CreateRepositorySnapshot());
        }
    }

    private RepositoryConfigSnapshot CreateRepositorySnapshot()
    {
        return new RepositoryConfigSnapshot(_data.Repositories.Select(ToModel).ToList());
    }

    private static void MigrateLegacyTasks(AppConfigDocument data)
    {
        foreach (var taskData in data.Tasks)
        {
            var repo = data.Repositories.FirstOrDefault(r =>
                string.Equals(r.Path, taskData.RepositoryPath, StringComparison.Ordinal));
            if (repo is not null && (repo.SourceDirectories is null || repo.SourceDirectories.Count == 0) &&
                taskData.SourceDirectories.Count > 0)
            {
                repo.SourceDirectories ??= [];
                foreach (var dir in taskData.SourceDirectories)
                    repo.SourceDirectories.Add(dir);
            }
        }

        data.Tasks = [];
    }

    private static void NormalizeRepositories(AppConfigDocument data)
    {
        data.Repositories ??= [];
        data.Tasks ??= [];
        foreach (var repo in data.Repositories)
        {
            repo.SourceDirectories ??= null;
            repo.Schedule ??= null;
            repo.PruneOptions ??= null;
        }
    }

    private static void NormalizeSettings(AppConfigDocument data)
    {
        data.Settings ??= new();
        data.Settings.DefaultExtractOptions ??= new();
        if (!string.IsNullOrWhiteSpace(data.Settings.BorgBinaryPath) &&
            string.IsNullOrWhiteSpace(data.Settings.Borg1BinaryPath))
        {
            data.Settings.Borg1BinaryPath = data.Settings.BorgBinaryPath;
            data.Settings.BorgBinaryPath = null;
        }
    }

    private void BackupExistingConfig()
    {
        if (!File.Exists(_configFilePath))
            return;

        var timestamp = DateTimeOffset.Now.ToString("yyyyMMddHHmmss");
        var backupPath = $"{_configFilePath}.{timestamp}.bak";
        for (var i = 2; File.Exists(backupPath); i++)
            backupPath = $"{_configFilePath}.{timestamp}.{i}.bak";
        File.Copy(_configFilePath, backupPath, false);
    }

    private static BorgRepository ToModel(RepositoryData data)
    {
        var locationKind = ResolveLocationKind(data);
        var borgUrl = !string.IsNullOrWhiteSpace(data.BorgUrl) ? data.BorgUrl : string.Empty;
        var path = locationKind == BorgRepositoryLocationKind.BorgUrl && !string.IsNullOrWhiteSpace(borgUrl)
            ? borgUrl
            : data.Path;

        var repo = new BorgRepository
        {
            Name = data.Name,
            ArchiveNamePrefix = data.ArchiveNamePrefix,
            Path = path,
            EncryptionMode = data.EncryptionMode,
            Borg2EncryptionMode = data.Borg2EncryptionMode,
            LocationKind = locationKind,
            BorgUrl = locationKind == BorgRepositoryLocationKind.BorgUrl
                ? string.IsNullOrWhiteSpace(borgUrl) ? data.Path : borgUrl
                : string.Empty,
            SshKeyPath = data.SshKeyPath,
            RateLimit = data.RateLimit,
            SshPort = data.SshPort,
            BorgVersion = data.BorgVersion,
            BorgRemotePath = data.BorgRemotePath,
            IsLocal = locationKind == BorgRepositoryLocationKind.LocalPath,
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
            repo.Schedule.DayOfWeek = Enum.TryParse<DayOfWeek>(data.Schedule.DayOfWeek, out var dow)
                ? dow
                : DayOfWeek.Monday;
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
        var locationKind = NormalizeLocationKind(repo);
        return new RepositoryData
        {
            Name = repo.Name,
            ArchiveNamePrefix = repo.ArchiveNamePrefix,
            Path = repo.Path,
            EncryptionMode = repo.EncryptionMode,
            Borg2EncryptionMode = repo.Borg2EncryptionMode,
            LocationKind = locationKind,
            BorgUrl = locationKind == BorgRepositoryLocationKind.BorgUrl
                ? string.IsNullOrWhiteSpace(repo.BorgUrl) ? repo.Path : repo.BorgUrl
                : string.Empty,
            SshKeyPath = repo.SshKeyPath,
            RateLimit = repo.RateLimit,
            SshPort = repo.SshPort,
            BorgVersion = repo.BorgVersion,
            BorgRemotePath = repo.BorgRemotePath,
            IsLocal = locationKind == BorgRepositoryLocationKind.LocalPath,
            SourceDirectories = repo.SourceDirectories.Count > 0 ? repo.SourceDirectories.ToList() : null,
            Mode = repo.Mode != BackupMode.Manual ? repo.Mode : null,
            Schedule = repo.Mode != BackupMode.Manual
                ? new BackupScheduleData
                {
                    Frequency = repo.Schedule.Frequency,
                    Hour = repo.Schedule.Hour,
                    Minute = repo.Schedule.Minute,
                    DayOfWeek = repo.Schedule.DayOfWeek.ToString(),
                    DayOfMonth = repo.Schedule.DayOfMonth,
                    IntervalHours = repo.Schedule.IntervalHours,
                    RunMissed = repo.Schedule.RunMissed,
                    RunPruneAfterBackup = repo.Schedule.RunPruneAfterBackup
                }
                : null,
            LastBackupAt = repo.LastBackupAt?.ToString("o"),
            PruneOptions = repo.PruneOptions.HasAnyRetention
                ? new PruneOptionsData
                {
                    KeepLast = repo.PruneOptions.KeepLast,
                    KeepHourly = repo.PruneOptions.KeepHourly,
                    KeepDaily = repo.PruneOptions.KeepDaily,
                    KeepWeekly = repo.PruneOptions.KeepWeekly,
                    KeepMonthly = repo.PruneOptions.KeepMonthly,
                    KeepYearly = repo.PruneOptions.KeepYearly,
                    CompactAfterPrune = repo.PruneOptions.CompactAfterPrune
                }
                : null
        };
    }

    public static BorgRepository Clone(BorgRepository repo)
    {
        var locationKind = NormalizeLocationKind(repo);
        var clone = new BorgRepository
        {
            Name = repo.Name,
            ArchiveNamePrefix = repo.ArchiveNamePrefix,
            Path = repo.Path,
            EncryptionMode = repo.EncryptionMode,
            Borg2EncryptionMode = repo.Borg2EncryptionMode,
            LocationKind = locationKind,
            BorgUrl = locationKind == BorgRepositoryLocationKind.BorgUrl
                ? string.IsNullOrWhiteSpace(repo.BorgUrl) ? repo.Path : repo.BorgUrl
                : string.Empty,
            SshKeyPath = repo.SshKeyPath,
            RateLimit = repo.RateLimit,
            SshPort = repo.SshPort,
            BorgVersion = repo.BorgVersion,
            BorgRemotePath = repo.BorgRemotePath,
            IsLocal = locationKind == BorgRepositoryLocationKind.LocalPath,
            Mode = repo.Mode,
            LastBackupAt = repo.LastBackupAt,
            Schedule = new BackupSchedule
            {
                Frequency = repo.Schedule?.Frequency ?? ScheduleFrequency.Daily,
                Hour = repo.Schedule?.Hour ?? 2,
                Minute = repo.Schedule?.Minute ?? 0,
                DayOfWeek = repo.Schedule?.DayOfWeek ?? DayOfWeek.Monday,
                DayOfMonth = repo.Schedule?.DayOfMonth ?? 1,
                IntervalHours = repo.Schedule?.IntervalHours ?? 6,
                RunMissed = repo.Schedule?.RunMissed ?? true,
                RunPruneAfterBackup = repo.Schedule?.RunPruneAfterBackup ?? false
            },
            PruneOptions = new PruneOptions
            {
                KeepLast = repo.PruneOptions?.KeepLast ?? 0,
                KeepHourly = repo.PruneOptions?.KeepHourly ?? 0,
                KeepDaily = repo.PruneOptions?.KeepDaily ?? 0,
                KeepWeekly = repo.PruneOptions?.KeepWeekly ?? 0,
                KeepMonthly = repo.PruneOptions?.KeepMonthly ?? 0,
                KeepYearly = repo.PruneOptions?.KeepYearly ?? 0,
                CompactAfterPrune = repo.PruneOptions?.CompactAfterPrune ?? true
            }
        };

        foreach (var dir in repo.SourceDirectories ?? [])
            clone.SourceDirectories.Add(dir);

        return clone;
    }

    private static BorgRepositoryLocationKind ResolveLocationKind(RepositoryData data)
    {
        if (data.LocationKind is { } locationKind)
            return locationKind;

        if (data.IsLocal)
            return BorgRepositoryLocationKind.LocalPath;

        var path = !string.IsNullOrWhiteSpace(data.BorgUrl) ? data.BorgUrl : data.Path;
        return data.BorgVersion == BorgVersion.Borg2 && BorgRepositoryUrlValidator.IsSupportedBorg2Url(path)
            ? BorgRepositoryLocationKind.BorgUrl
            : BorgRepositoryLocationKind.SshFields;
    }

    private static BorgRepositoryLocationKind NormalizeLocationKind(BorgRepository repo)
    {
        return repo.LocationKind == BorgRepositoryLocationKind.LocalPath && !repo.IsLocal
            ? BorgRepositoryLocationKind.SshFields
            : repo.LocationKind;
    }
}
