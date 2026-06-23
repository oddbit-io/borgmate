using BorgMate.Domain.Repositories;
using RepositoryViewModel = BorgMate.Presentation.ViewModels.RepositoryViewModel;

namespace BorgMate.Tests.Domain;

public class BorgRepositoryTests
{
    [Fact]
    public void PersistedModel_Defaults_AreCorrect()
    {
        var repo = new BorgRepository();

        Assert.Equal(string.Empty, repo.Name);
        Assert.Equal(string.Empty, repo.Path);
        Assert.True(repo.IsLocal);
        Assert.False(repo.IsSsh);
        Assert.Equal(22, repo.SshPort);
        Assert.Equal(BorgVersion.Borg1, repo.BorgVersion);
        Assert.Equal(BorgEncryptionMode.RepokeyBlake2, repo.EncryptionMode);
        Assert.Equal(BackupMode.Manual, repo.Mode);
        Assert.Empty(repo.SourceDirectories);
        Assert.Null(repo.LastBackupAt);
    }

    [Fact]
    public void PersistedModel_DoesNotExposeRuntimeSecrets()
    {
        var properties = typeof(BorgRepository).GetProperties().Select(p => p.Name).ToHashSet();

        Assert.DoesNotContain("Passphrase", properties);
        Assert.DoesNotContain("WrongPassphrase", properties);
        Assert.DoesNotContain("SshKeyPassphrase", properties);
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
    public void RepositoryViewModel_PropertyChanged_Fires_ForPresentationProperties()
    {
        var repo = new RepositoryViewModel();
        var changed = new List<string?>();
        repo.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        repo.Name = "test";
        repo.Path = "/repo";
        repo.IsBusy = true;
        repo.RateLimit = 100;

        Assert.Contains(nameof(RepositoryViewModel.Name), changed);
        Assert.Contains(nameof(RepositoryViewModel.Path), changed);
        Assert.Contains(nameof(RepositoryViewModel.IsBusy), changed);
        Assert.Contains(nameof(RepositoryViewModel.RateLimit), changed);
    }

    [Fact]
    public void RepositoryViewModel_RefreshScheduleDisplay_FiresPropertyChanged()
    {
        var repo = new RepositoryViewModel();
        var changed = new List<string?>();
        repo.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        repo.RefreshScheduleDisplay();

        Assert.Contains(nameof(RepositoryViewModel.IsScheduled), changed);
        Assert.Contains(nameof(RepositoryViewModel.LastBackupAt), changed);
    }
}
