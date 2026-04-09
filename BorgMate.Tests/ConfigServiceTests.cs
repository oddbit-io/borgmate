using BorgMate.Models;
using BorgMate.Services.Config;

namespace BorgMate.Tests;

public class ConfigServiceTests
{
    [Fact]
    public void ToModel_BasicFields()
    {
        var data = new RepositoryData
        {
            Name = "My Repo",
            Path = "/data/borg",
            IsLocal = true,
            EncryptionMode = BorgEncryptionMode.RepokeyBlake2,
            SshKeyPath = "/home/user/.ssh/id_ed25519",
            SshPort = 2222,
            RateLimit = 1000
        };

        var repo = ConfigService.ToModel(data);

        Assert.Equal("My Repo", repo.Name);
        Assert.Equal("/data/borg", repo.Path);
        Assert.True(repo.IsLocal);
        Assert.Equal(BorgEncryptionMode.RepokeyBlake2, repo.EncryptionMode);
        Assert.Equal("/home/user/.ssh/id_ed25519", repo.SshKeyPath);
        Assert.Equal(2222, repo.SshPort);
        Assert.Equal(1000, repo.RateLimit);
    }

    [Fact]
    public void ToModel_SourceDirectories()
    {
        var data = new RepositoryData
        {
            Name = "test", Path = "/test",
            SourceDirectories = ["/home/user/docs", "/home/user/photos"]
        };

        var repo = ConfigService.ToModel(data);

        Assert.Equal(2, repo.SourceDirectories.Count);
        Assert.Contains("/home/user/docs", repo.SourceDirectories);
        Assert.Contains("/home/user/photos", repo.SourceDirectories);
    }

    [Fact]
    public void ToModel_Schedule()
    {
        var data = new RepositoryData
        {
            Name = "test", Path = "/test",
            Mode = BackupMode.Scheduled,
            Schedule = new BackupScheduleData
            {
                Frequency = ScheduleFrequency.Weekly,
                Hour = 3, Minute = 30,
                DayOfWeek = "Friday",
                IntervalHours = 0,
                DayOfMonth = 0,
                RunMissed = false
            }
        };

        var repo = ConfigService.ToModel(data);

        Assert.Equal(BackupMode.Scheduled, repo.Mode);
        Assert.Equal(ScheduleFrequency.Weekly, repo.Schedule.Frequency);
        Assert.Equal(3, repo.Schedule.Hour);
        Assert.Equal(30, repo.Schedule.Minute);
        Assert.Equal(DayOfWeek.Friday, repo.Schedule.DayOfWeek);
        Assert.False(repo.Schedule.RunMissed);
    }

    [Fact]
    public void ToModel_LastBackupAt()
    {
        var data = new RepositoryData
        {
            Name = "test", Path = "/test",
            LastBackupAt = "2026-03-15T14:30:00"
        };

        var repo = ConfigService.ToModel(data);

        Assert.NotNull(repo.LastBackupAt);
        Assert.Equal(new DateTime(2026, 3, 15, 14, 30, 0), repo.LastBackupAt);
    }

    [Fact]
    public void ToModel_ManualMode_Default()
    {
        var data = new RepositoryData { Name = "test", Path = "/test" };
        var repo = ConfigService.ToModel(data);

        Assert.Equal(BackupMode.Manual, repo.Mode);
    }

    [Fact]
    public void FromModel_Roundtrip()
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

        var data = ConfigService.FromModel(original);
        var restored = ConfigService.ToModel(data);

        Assert.Equal(original.Name, restored.Name);
        Assert.Equal(original.Path, restored.Path);
        Assert.Equal(original.IsLocal, restored.IsLocal);
        Assert.Equal(original.EncryptionMode, restored.EncryptionMode);
        Assert.Equal(original.SshKeyPath, restored.SshKeyPath);
        Assert.Equal(original.SshPort, restored.SshPort);
        Assert.Equal(original.Mode, restored.Mode);
        Assert.Equal(original.Schedule.Frequency, restored.Schedule.Frequency);
        Assert.Equal(original.Schedule.Hour, restored.Schedule.Hour);
        Assert.Equal(original.Schedule.Minute, restored.Schedule.Minute);
        Assert.Single(restored.SourceDirectories);
    }

    [Fact]
    public void FromModel_Manual_NoScheduleOrMode()
    {
        var repo = new BorgRepository { Name = "test", Path = "/test", Mode = BackupMode.Manual };
        var data = ConfigService.FromModel(repo);

        Assert.Null(data.Mode);
        Assert.Null(data.Schedule);
    }

    [Fact]
    public void FromModel_EmptySourceDirs_Null()
    {
        var repo = new BorgRepository { Name = "test", Path = "/test" };
        var data = ConfigService.FromModel(repo);

        Assert.Null(data.SourceDirectories);
    }
}
