using BorgMate.Models;

namespace BorgMate.Tests;

public class BorgRepositoryTests
{
    [Fact]
    public void Defaults_AreCorrect()
    {
        var repo = new BorgRepository();
        Assert.Equal(string.Empty, repo.Name);
        Assert.Equal(string.Empty, repo.Path);
        Assert.True(repo.IsLocal);
        Assert.False(repo.IsSsh);
        Assert.False(repo.IsBusy);
        Assert.False(repo.WrongPassphrase);
        Assert.Equal(22, repo.SshPort);
        Assert.Equal(BorgVersion.Borg1, repo.BorgVersion);
        Assert.Equal(BorgEncryptionMode.RepokeyBlake2, repo.EncryptionMode);
        Assert.Equal(BackupMode.Manual, repo.Mode);
        Assert.Empty(repo.SourceDirectories);
        Assert.Null(repo.LastBackupAt);
        Assert.Null(repo.SshKeyPassphrase);
    }

    [Fact]
    public void IsSsh_Inverse_Of_IsLocal()
    {
        var repo = new BorgRepository { IsLocal = true };
        Assert.False(repo.IsSsh);

        repo.IsLocal = false;
        Assert.True(repo.IsSsh);
    }

    [Fact]
    public void IsLocal_Change_NotifiesIsSsh()
    {
        var repo = new BorgRepository();
        var changed = new List<string>();
        repo.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

        repo.IsLocal = false;

        Assert.Contains(nameof(BorgRepository.IsLocal), changed);
        Assert.Contains(nameof(BorgRepository.IsSsh), changed);
    }

    [Fact]
    public void IsScheduled_False_WhenManual()
    {
        var repo = new BorgRepository { Mode = BackupMode.Manual };
        repo.SourceDirectories.Add("/data");
        Assert.False(repo.IsScheduled);
    }

    [Fact]
    public void IsScheduled_False_WhenScheduledButNoSourceDirs()
    {
        var repo = new BorgRepository { Mode = BackupMode.Scheduled };
        Assert.False(repo.IsScheduled);
    }

    [Fact]
    public void IsScheduled_True_WhenScheduledWithSourceDirs()
    {
        var repo = new BorgRepository { Mode = BackupMode.Scheduled };
        repo.SourceDirectories.Add("/data");
        Assert.True(repo.IsScheduled);
    }

    [Fact]
    public void PropertyChanged_Fires_ForObservableProperties()
    {
        var repo = new BorgRepository();
        var changed = new List<string>();
        repo.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

        repo.Name = "test";
        repo.Path = "/repo";
        repo.IsBusy = true;
        repo.WrongPassphrase = true;
        repo.RateLimit = 100;

        Assert.Contains(nameof(BorgRepository.Name), changed);
        Assert.Contains(nameof(BorgRepository.Path), changed);
        Assert.Contains(nameof(BorgRepository.IsBusy), changed);
        Assert.Contains(nameof(BorgRepository.WrongPassphrase), changed);
        Assert.Contains(nameof(BorgRepository.RateLimit), changed);
    }

    [Fact]
    public void RefreshScheduleDisplay_FiresPropertyChanged()
    {
        var repo = new BorgRepository();
        var changed = new List<string>();
        repo.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

        repo.RefreshScheduleDisplay();

        Assert.Contains(nameof(BorgRepository.ScheduleDisplay), changed);
        Assert.Contains(nameof(BorgRepository.ScheduleTooltip), changed);
        Assert.Contains(nameof(BorgRepository.IsScheduled), changed);
    }
}
