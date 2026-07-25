using System.Text.Json;
using BorgMate.Domain.Repositories;
using BorgMate.Domain.Settings;
using BorgMate.Persistence.Configuration;
using BackupSchedule = BorgMate.Domain.Repositories.BackupSchedule;

namespace BorgMate.Tests.Persistence;

public class JsonAppConfigStoreTests
{
    [Fact]
    public async Task LoadAsync_LoadsPreRefactorRepositoryConfigShape()
    {
        var configPath = CreateTempConfigPath();
        await File.WriteAllTextAsync(configPath, """
        {
          "Settings": {
            "Language": "en"
          },
          "Repositories": [
            {
              "Name": "Remote Repo",
              "ArchiveNamePrefix": "remote",
              "Path": "user@example.com:/srv/borg",
              "EncryptionMode": "repokey-blake2",
              "SshKeyPath": "/Users/user/.ssh/id_ed25519",
              "RateLimit": 128,
              "SshPort": 2222,
              "BorgVersion": "borg1",
              "BorgRemotePath": "/usr/local/bin/borg",
              "IsLocal": false,
              "SourceDirectories": [
                "/Users/user/Documents"
              ],
              "Schedule": {
                "Frequency": "Weekly",
                "Hour": 3,
                "Minute": 15,
                "DayOfWeek": "Friday",
                "DayOfMonth": 1,
                "IntervalHours": 6,
                "RunMissed": false,
                "RunPruneAfterBackup": true
              },
              "Mode": "Scheduled",
              "LastBackupAt": "2026-06-01T02:03:04.0000000",
              "PruneOptions": {
                "KeepLast": 2,
                "KeepHourly": 0,
                "KeepDaily": 7,
                "KeepWeekly": 4,
                "KeepMonthly": 0,
                "KeepYearly": 0,
                "CompactAfterPrune": true
              }
            }
          ],
          "Tasks": []
        }
        """);

        var store = new JsonAppConfigStore(configPath);
        var result = await ((IRepositoryConfigStore)store).LoadAsync();

        Assert.True(result.IsT0, result.IsT1 ? result.AsT1.ToString() : string.Empty);
        var repo = Assert.Single(result.AsT0.Repositories);
        Assert.Equal("Remote Repo", repo.Name);
        Assert.Equal("remote", repo.ArchiveNamePrefix);
        Assert.Equal("user@example.com:/srv/borg", repo.Path);
        Assert.False(repo.IsLocal);
        Assert.Equal(BorgRepositoryLocationKind.SshFields, repo.LocationKind);
        Assert.Equal(BorgEncryptionMode.RepokeyBlake2, repo.EncryptionMode);
        Assert.Equal("/Users/user/.ssh/id_ed25519", repo.SshKeyPath);
        Assert.Equal(2222, repo.SshPort);
        Assert.Equal(128, repo.RateLimit);
        Assert.Single(repo.SourceDirectories);
        Assert.Equal(BackupMode.Scheduled, repo.Mode);
        Assert.Equal(ScheduleFrequency.Weekly, repo.Schedule.Frequency);
        Assert.Equal(DayOfWeek.Friday, repo.Schedule.DayOfWeek);
        Assert.False(repo.Schedule.RunMissed);
        Assert.True(repo.Schedule.RunPruneAfterBackup);
        Assert.Equal(2, repo.PruneOptions.KeepLast);
        Assert.Equal(7, repo.PruneOptions.KeepDaily);
        Assert.Equal(new DateTime(2026, 6, 1, 2, 3, 4), repo.LastBackupAt);
    }

    [Fact]
    public async Task LoadAsync_MigratesLegacyBorgBinaryPathToBorg1Path()
    {
        var configPath = CreateTempConfigPath();
        await File.WriteAllTextAsync(configPath, """
        {
          "Settings": {
            "BorgBinaryPath": "/opt/borg/bin/borg"
          },
          "Repositories": [],
          "Tasks": []
        }
        """);
        var store = new JsonAppConfigStore(configPath);

        var load = await ((ISettingsStore)store).LoadAsync();

        Assert.True(load.IsT0, load.IsT1 ? load.AsT1.ToString() : string.Empty);
        Assert.Null(load.AsT0.Settings.BorgBinaryPath);
        Assert.Equal("/opt/borg/bin/borg", load.AsT0.Settings.Borg1BinaryPath);
    }

    [Fact]
    public async Task LoadAsync_LegacyBorg2UrlPath_DetectsRawBorgUrlLocation()
    {
        var configPath = CreateTempConfigPath();
        await File.WriteAllTextAsync(configPath, """
        {
          "Settings": {},
          "Repositories": [
            {
              "Name": "Cloud",
              "Path": "s3:bucket/path",
              "BorgVersion": "borg2",
              "IsLocal": false
            }
          ],
          "Tasks": []
        }
        """);
        var store = new JsonAppConfigStore(configPath);

        var load = await ((IRepositoryConfigStore)store).LoadAsync();

        Assert.True(load.IsT0, load.IsT1 ? load.AsT1.ToString() : string.Empty);
        var repo = Assert.Single(load.AsT0.Repositories);
        Assert.Equal(BorgRepositoryLocationKind.BorgUrl, repo.LocationKind);
        Assert.Equal("s3:bucket/path", repo.Path);
        Assert.Equal("s3:bucket/path", repo.BorgUrl);
        Assert.False(repo.IsLocal);
    }

    [Fact]
    public async Task SaveAsync_WritesDurableRepositoryAndTransientState()
    {
        var configPath = CreateTempConfigPath();
        var store = new JsonAppConfigStore(configPath);
        var repo = new BorgRepository
        {
            Name = "Scheduled",
            Path = "/repos/main",
            Mode = BackupMode.Scheduled,
            LastBackupAt = new DateTime(2026, 6, 2, 1, 2, 3)
        };
        repo.SourceDirectories.Add("/home/user");
        repo.Schedule.Frequency = ScheduleFrequency.Monthly;
        repo.Schedule.DayOfWeek = DayOfWeek.Thursday;
        repo.PruneOptions.KeepDaily = 5;

        await store.AddAsync(repo);
        var save = await store.SaveAsync();

        Assert.True(save.IsT0);
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(configPath));
        var persisted = document.RootElement.GetProperty("Repositories")[0];
        Assert.Equal("Scheduled", persisted.GetProperty("Name").GetString());
        Assert.Equal("Scheduled", persisted.GetProperty("Mode").GetString());
        Assert.Equal("Thursday", persisted.GetProperty("Schedule").GetProperty("DayOfWeek").GetString());
        Assert.False(persisted.TryGetProperty("LastBackupAt", out _));
        Assert.Equal(5, persisted.GetProperty("PruneOptions").GetProperty("KeepDaily").GetInt32());

        using var stateDocument = JsonDocument.Parse(await File.ReadAllTextAsync(GetStatePath(configPath)));
        var lastBackupAt = stateDocument.RootElement
            .GetProperty("LastBackupAtByRepositoryPath")
            .GetProperty("/repos/main")
            .GetDateTime();
        Assert.Equal(new DateTime(2026, 6, 2, 1, 2, 3), lastBackupAt);

        var reloaded = await ((IRepositoryConfigStore)new JsonAppConfigStore(configPath)).LoadAsync();
        Assert.True(reloaded.IsT0, reloaded.IsT1 ? reloaded.AsT1.ToString() : string.Empty);
        Assert.Equal(new DateTime(2026, 6, 2, 1, 2, 3), Assert.Single(reloaded.AsT0.Repositories).LastBackupAt);
    }

    [Fact]
    public async Task SaveAsync_WritesBorg2RawUrlLocationShape()
    {
        var configPath = CreateTempConfigPath();
        var store = new JsonAppConfigStore(configPath);
        var repo = new BorgRepository
        {
            Name = "Cloud",
            Path = "s3:bucket/path",
            BorgUrl = "s3:bucket/path",
            BorgVersion = BorgVersion.Borg2,
            LocationKind = BorgRepositoryLocationKind.BorgUrl,
            Borg2EncryptionMode = Borg2EncryptionMode.RepokeyChacha20Poly1305
        };

        await store.AddAsync(repo);
        var save = await store.SaveAsync();

        Assert.True(save.IsT0);
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(configPath));
        var persisted = document.RootElement.GetProperty("Repositories")[0];
        Assert.Equal("borgUrl", persisted.GetProperty("LocationKind").GetString());
        Assert.Equal("s3:bucket/path", persisted.GetProperty("BorgUrl").GetString());
        Assert.Equal("s3:bucket/path", persisted.GetProperty("Path").GetString());
        Assert.Equal("repokey-chacha20-poly1305", persisted.GetProperty("Borg2EncryptionMode").GetString());
        Assert.False(persisted.GetProperty("IsLocal").GetBoolean());
    }

    [Theory]
    [InlineData("")]
    [InlineData("""
        ,
            "DefaultExtractOptions": null
        """)]
    public async Task LoadAsync_OldSettingsUseCompatibilityExtractDefaults(string extraSettingsJson)
    {
        var configPath = CreateTempConfigPath();
        await File.WriteAllTextAsync(configPath, $$"""
        {
          "Settings": {
            "Language": "en"
            {{extraSettingsJson}}
          },
          "Repositories": [],
          "Tasks": []
        }
        """);
        var store = new JsonAppConfigStore(configPath);

        var load = await ((ISettingsStore)store).LoadAsync();

        Assert.True(load.IsT0, load.IsT1 ? load.AsT1.ToString() : string.Empty);
        var options = load.AsT0.Settings.DefaultExtractOptions;
        Assert.True(options.NoFlags);
        Assert.True(options.NoAcls);
        Assert.True(options.NoXattrs);
        Assert.False(options.Sparse);
    }

    [Fact]
    public async Task SaveAsync_WritesExtractDefaults()
    {
        var configPath = CreateTempConfigPath();
        var store = new JsonAppConfigStore(configPath);
        var config = store.Load();
        config.Settings.DefaultExtractOptions = new BorgExtractOptions
        {
            NoFlags = false,
            NoAcls = true,
            NoXattrs = false,
            Sparse = true
        };

        var save = await store.SaveAsync();

        Assert.True(save.IsT0);
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(configPath));
        var options = document.RootElement.GetProperty("Settings").GetProperty("DefaultExtractOptions");
        Assert.False(options.GetProperty("NoFlags").GetBoolean());
        Assert.True(options.GetProperty("NoAcls").GetBoolean());
        Assert.False(options.GetProperty("NoXattrs").GetBoolean());
        Assert.True(options.GetProperty("Sparse").GetBoolean());
    }

    [Fact]
    public async Task SaveAsync_DoesNotOverwriteExistingConfigAfterLoadFailure()
    {
        var configPath = CreateTempConfigPath();
        const string corruptConfig = "{ not json";
        await File.WriteAllTextAsync(configPath, corruptConfig);
        var store = new JsonAppConfigStore(configPath);

        var load = await ((ISettingsStore)store).LoadAsync();
        var save = await store.SaveAsync();

        Assert.True(load.IsT1);
        Assert.True(save.IsT1);
        Assert.Equal(corruptConfig, await File.ReadAllTextAsync(configPath));
    }

    [Fact]
    public async Task SaveAsync_CreatesRollingBackupBeforeOverwritingExistingConfig()
    {
        var configPath = CreateTempConfigPath();
        const string existingConfig = """
        {
          "Settings": {},
          "Repositories": [
            {
              "Name": "Existing",
              "Path": "/repos/existing",
              "EncryptionMode": "repokey-blake2",
              "SshKeyPath": "",
              "RateLimit": 0,
              "SshPort": 22,
              "BorgVersion": "borg1",
              "BorgRemotePath": "",
              "IsLocal": true,
              "SourceDirectories": null,
              "Schedule": null,
              "Mode": null,
              "LastBackupAt": null,
              "PruneOptions": null
            }
          ],
          "Tasks": []
        }
        """;
        await File.WriteAllTextAsync(configPath, existingConfig);
        var store = new JsonAppConfigStore(configPath);
        var load = await ((IRepositoryConfigStore)store).LoadAsync();
        Assert.True(load.IsT0, load.IsT1 ? load.AsT1.ToString() : string.Empty);
        var repo = Assert.Single(load.AsT0.Repositories);
        repo.ArchiveNamePrefix = "changed";

        await store.UpdateAsync(repo);
        var save = await store.SaveAsync();

        Assert.True(save.IsT0);
        Assert.Equal(existingConfig, await File.ReadAllTextAsync(configPath + ".bak"));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(configPath)!, "config.json.*.bak"));

        var previousConfig = await File.ReadAllTextAsync(configPath);
        repo.ArchiveNamePrefix = "changed-again";
        await store.UpdateAsync(repo);
        Assert.True((await store.SaveAsync()).IsT0);
        Assert.Equal(previousConfig, await File.ReadAllTextAsync(configPath + ".bak"));
    }

    [Fact]
    public async Task SaveAsync_TransientChangeDoesNotRewriteDurableConfig()
    {
        var configPath = CreateTempConfigPath();
        var store = new JsonAppConfigStore(configPath);
        var config = store.Load();
        config.Settings.Language = "en";

        Assert.True((await store.SaveAsync()).IsT0);
        var durableConfig = await File.ReadAllTextAsync(configPath);

        config.Settings.WindowX = 120;
        config.Settings.WindowY = 80;
        config.Settings.IsMaximized = true;
        config.Settings.SidebarExpanded = true;
        Assert.True((await store.SaveAsync()).IsT0);

        Assert.Equal(durableConfig, await File.ReadAllTextAsync(configPath));
        Assert.False(File.Exists(configPath + ".bak"));
        using var stateDocument = JsonDocument.Parse(await File.ReadAllTextAsync(GetStatePath(configPath)));
        Assert.Equal(120, stateDocument.RootElement.GetProperty("WindowX").GetDouble());
        Assert.Equal(80, stateDocument.RootElement.GetProperty("WindowY").GetDouble());
        Assert.True(stateDocument.RootElement.GetProperty("IsMaximized").GetBoolean());
        Assert.True(stateDocument.RootElement.GetProperty("SidebarExpanded").GetBoolean());

        var reloaded = await ((ISettingsStore)new JsonAppConfigStore(configPath)).LoadAsync();
        Assert.True(reloaded.IsT0, reloaded.IsT1 ? reloaded.AsT1.ToString() : string.Empty);
        Assert.Equal(120, reloaded.AsT0.Settings.WindowX);
        Assert.Equal(80, reloaded.AsT0.Settings.WindowY);
        Assert.True(reloaded.AsT0.Settings.IsMaximized);
        Assert.True(reloaded.AsT0.Settings.SidebarExpanded);
    }

    [Fact]
    public async Task SaveAsync_MigratesTransientStateAndDeletesTimestampedBackups()
    {
        var configPath = CreateTempConfigPath();
        const string legacyConfig = """
        {
          "Settings": {
            "Language": "ru",
            "WindowX": 42,
            "WindowY": 24,
            "WindowWidth": 1280,
            "WindowHeight": 720,
            "IsMaximized": true,
            "SidebarExpanded": true
          },
          "Repositories": [
            {
              "Name": "Main",
              "Path": "/repos/main",
              "LastBackupAt": "2026-07-24T12:34:56.0000000"
            }
          ],
          "Tasks": []
        }
        """;
        await File.WriteAllTextAsync(configPath, legacyConfig);
        var directory = Path.GetDirectoryName(configPath)!;
        await File.WriteAllTextAsync(Path.Combine(directory, "config.json.20260724010101.bak"), "old");
        await File.WriteAllTextAsync(Path.Combine(directory, "config.json.20260725010101.2.bak"), "older");
        var store = new JsonAppConfigStore(configPath);

        var config = store.Load();
        var repository = store.FindByPath("/repos/main");

        Assert.Equal(42, config.Settings.WindowX);
        Assert.Equal(24, config.Settings.WindowY);
        Assert.Equal(1280, config.Settings.WindowWidth);
        Assert.Equal(720, config.Settings.WindowHeight);
        Assert.True(config.Settings.IsMaximized);
        Assert.True(config.Settings.SidebarExpanded);
        Assert.Equal(new DateTime(2026, 7, 24, 12, 34, 56), repository?.LastBackupAt);
        Assert.True((await store.SaveAsync()).IsT0);

        using var durableDocument = JsonDocument.Parse(await File.ReadAllTextAsync(configPath));
        var settings = durableDocument.RootElement.GetProperty("Settings");
        Assert.False(settings.TryGetProperty("WindowX", out _));
        Assert.False(settings.TryGetProperty("SidebarExpanded", out _));
        Assert.False(durableDocument.RootElement.GetProperty("Repositories")[0]
            .TryGetProperty("LastBackupAt", out _));

        using var stateDocument = JsonDocument.Parse(await File.ReadAllTextAsync(GetStatePath(configPath)));
        Assert.Equal(42, stateDocument.RootElement.GetProperty("WindowX").GetDouble());
        Assert.Equal(new DateTime(2026, 7, 24, 12, 34, 56), stateDocument.RootElement
            .GetProperty("LastBackupAtByRepositoryPath")
            .GetProperty("/repos/main")
            .GetDateTime());
        Assert.Equal(legacyConfig, await File.ReadAllTextAsync(configPath + ".bak"));
        Assert.Empty(Directory.GetFiles(directory, "config.json.*.bak"));
    }

    [Fact]
    public async Task LoadAsync_CorruptTransientStateDoesNotBlockConfig()
    {
        var configPath = CreateTempConfigPath();
        await File.WriteAllTextAsync(configPath, """
        {
          "Settings": {
            "Language": "en"
          },
          "Repositories": [],
          "Tasks": []
        }
        """);
        await File.WriteAllTextAsync(GetStatePath(configPath), "{ not json");
        var store = new JsonAppConfigStore(configPath);

        var load = await ((ISettingsStore)store).LoadAsync();

        Assert.True(load.IsT0, load.IsT1 ? load.AsT1.ToString() : string.Empty);
        Assert.Equal("en", load.AsT0.Settings.Language);
        Assert.Null(load.AsT0.Settings.WindowX);
    }

    [Fact]
    public void Clone_BasicFields()
    {
        var original = new BorgRepository
        {
            Name = "My Repo",
            Path = "/data/borg",
            IsLocal = true,
            EncryptionMode = BorgEncryptionMode.RepokeyBlake2,
            SshKeyPath = "/home/user/.ssh/id_ed25519",
            SshPort = 2222,
            RateLimit = 1000
        };

        var repo = JsonAppConfigStore.Clone(original);

        Assert.Equal("My Repo", repo.Name);
        Assert.Equal("/data/borg", repo.Path);
        Assert.True(repo.IsLocal);
        Assert.Equal(BorgEncryptionMode.RepokeyBlake2, repo.EncryptionMode);
        Assert.Equal("/home/user/.ssh/id_ed25519", repo.SshKeyPath);
        Assert.Equal(2222, repo.SshPort);
        Assert.Equal(1000, repo.RateLimit);
    }

    [Fact]
    public void Clone_SourceDirectories()
    {
        var original = new BorgRepository { Name = "test", Path = "/test" };
        original.SourceDirectories.Add("/home/user/docs");
        original.SourceDirectories.Add("/home/user/photos");

        var repo = JsonAppConfigStore.Clone(original);

        Assert.Equal(2, repo.SourceDirectories.Count);
        Assert.Contains("/home/user/docs", repo.SourceDirectories);
        Assert.Contains("/home/user/photos", repo.SourceDirectories);
    }

    [Fact]
    public void Clone_Schedule()
    {
        var original = new BorgRepository
        {
            Name = "test",
            Path = "/test",
            Mode = BackupMode.Scheduled,
            Schedule = new BackupSchedule
            {
                Frequency = ScheduleFrequency.Weekly,
                Hour = 3,
                Minute = 30,
                DayOfWeek = DayOfWeek.Friday,
                IntervalHours = 0,
                DayOfMonth = 0,
                RunMissed = false
            }
        };

        var repo = JsonAppConfigStore.Clone(original);

        Assert.Equal(BackupMode.Scheduled, repo.Mode);
        Assert.Equal(ScheduleFrequency.Weekly, repo.Schedule.Frequency);
        Assert.Equal(3, repo.Schedule.Hour);
        Assert.Equal(30, repo.Schedule.Minute);
        Assert.Equal(DayOfWeek.Friday, repo.Schedule.DayOfWeek);
        Assert.False(repo.Schedule.RunMissed);
    }

    [Fact]
    public void Clone_LastBackupAt()
    {
        var original = new BorgRepository
        {
            Name = "test",
            Path = "/test",
            LastBackupAt = new DateTime(2026, 3, 15, 14, 30, 0)
        };

        var repo = JsonAppConfigStore.Clone(original);

        Assert.NotNull(repo.LastBackupAt);
        Assert.Equal(new DateTime(2026, 3, 15, 14, 30, 0), repo.LastBackupAt);
    }

    [Fact]
    public void Clone_ManualMode_Default()
    {
        var repo = JsonAppConfigStore.Clone(new BorgRepository { Name = "test", Path = "/test" });

        Assert.Equal(BackupMode.Manual, repo.Mode);
    }

    [Fact]
    public void Clone_ProducesIndependentNestedInstances()
    {
        var original = new BorgRepository
        {
            Name = "My Repo",
            Path = "user@host:/data/borg",
            IsLocal = false,
            EncryptionMode = BorgEncryptionMode.RepokeyBlake2,
            SshKeyPath = "~/.ssh/id_ed25519",
            SshPort = 2222,
            BorgVersion = BorgVersion.Borg1,
            Mode = BackupMode.Scheduled,
            LastBackupAt = new DateTime(2026, 3, 15, 14, 30, 0)
        };
        original.SourceDirectories.Add("/home/user/docs");
        original.Schedule.Frequency = ScheduleFrequency.Daily;
        original.Schedule.Hour = 2;
        original.Schedule.Minute = 30;

        var restored = JsonAppConfigStore.Clone(original);
        original.SourceDirectories.Add("/other");
        original.Schedule.Hour = 5;

        Assert.Equal("My Repo", restored.Name);
        Assert.Equal("user@host:/data/borg", restored.Path);
        Assert.Equal(2, restored.Schedule.Hour);
        Assert.Single(restored.SourceDirectories);
    }

    [Fact]
    public void Clone_CopiesBorg2RawUrlLocation()
    {
        var original = new BorgRepository
        {
            Name = "Cloud",
            Path = "rest://repo.example.com/backups",
            BorgUrl = "rest://repo.example.com/backups",
            BorgVersion = BorgVersion.Borg2,
            LocationKind = BorgRepositoryLocationKind.BorgUrl,
            Borg2EncryptionMode = Borg2EncryptionMode.KeyfileAesOcb
        };

        var restored = JsonAppConfigStore.Clone(original);

        Assert.Equal(BorgRepositoryLocationKind.BorgUrl, restored.LocationKind);
        Assert.Equal("rest://repo.example.com/backups", restored.Path);
        Assert.Equal("rest://repo.example.com/backups", restored.BorgUrl);
        Assert.Equal(Borg2EncryptionMode.KeyfileAesOcb, restored.Borg2EncryptionMode);
        Assert.False(restored.IsLocal);
    }

    [Fact]
    public void Clone_CopiesSmbShareLocation()
    {
        var original = new BorgRepository
        {
            Name = "NAS",
            Path = "smb://bob@nas.local/backups/laptop",
            LocationKind = BorgRepositoryLocationKind.SmbShare,
            IsLocal = false
        };

        var restored = JsonAppConfigStore.Clone(original);

        Assert.Equal(BorgRepositoryLocationKind.SmbShare, restored.LocationKind);
        Assert.Equal("smb://bob@nas.local/backups/laptop", restored.Path);
        Assert.False(restored.IsLocal);
        Assert.False(restored.IsSsh);
    }

    private static string CreateTempConfigPath()
    {
        var directory = Path.Combine(Path.GetTempPath(), "BorgMate.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "config.json");
    }

    private static string GetStatePath(string configPath)
    {
        return Path.Combine(Path.GetDirectoryName(configPath)!, "state.json");
    }
}
