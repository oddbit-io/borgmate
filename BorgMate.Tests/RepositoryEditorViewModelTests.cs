using BorgMate.Models;
using BorgMate.Services;
using BorgMate.Services.Borg;
using BorgMate.Services.Config;
using BorgMate.Services.UI;
using BorgMate.ViewModels;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace BorgMate.Tests;

public class RepositoryEditorViewModelTests
{
    private static RepositoryEditorViewModel CreateVm() =>
        new(new BorgServiceFactory(Substitute.For<ILoggerFactory>(), new AppSettings(), new SshAgentHelper(Substitute.For<ILogger<SshAgentHelper>>(), null, new WslHelper(Substitute.For<ILogger<WslHelper>>())), new WslHelper(Substitute.For<ILogger<WslHelper>>())),
            Substitute.For<IStatusService>(),
            new FilePickerService());

    // --- Factory methods ---

    [Fact]
    public void ForNew_SetsDefaults()
    {
        var vm = RepositoryEditorViewModel.ForNew(
            new BorgServiceFactory(Substitute.For<ILoggerFactory>(), new AppSettings(), new SshAgentHelper(Substitute.For<ILogger<SshAgentHelper>>(), null, new WslHelper(Substitute.For<ILogger<WslHelper>>())), new WslHelper(Substitute.For<ILogger<WslHelper>>())),
            Substitute.For<IStatusService>(),
            new FilePickerService());

        Assert.True(vm.IsNew);
        Assert.False(vm.IsOpen);
        Assert.True(vm.Repository.IsLocal);
        Assert.Equal(BorgEncryptionMode.RepokeyBlake2, vm.Repository.EncryptionMode);
    }

    [Fact]
    public void ForOpen_SetsDefaults()
    {
        var vm = RepositoryEditorViewModel.ForOpen(
            new BorgServiceFactory(Substitute.For<ILoggerFactory>(), new AppSettings(), new SshAgentHelper(Substitute.For<ILogger<SshAgentHelper>>(), null, new WslHelper(Substitute.For<ILogger<WslHelper>>())), new WslHelper(Substitute.For<ILogger<WslHelper>>())),
            Substitute.For<IStatusService>(),
            new FilePickerService());

        Assert.True(vm.IsOpen);
        Assert.False(vm.IsNew);
    }

    [Fact]
    public void ForEdit_LoadsRepoFields()
    {
        var repo = new BorgRepository
        {
            Name = "My Repo",
            Path = "user@host:/data/borg",
            IsLocal = false,
            SshKeyPath = "~/.ssh/id_ed25519",
            SshPort = 2222,
            Mode = BackupMode.Scheduled
        };
        repo.Schedule.Frequency = ScheduleFrequency.Daily;
        repo.Schedule.Hour = 3;
        repo.SourceDirectories.Add("/home/user/docs");

        var vm = RepositoryEditorViewModel.ForEdit(
            new BorgServiceFactory(Substitute.For<ILoggerFactory>(), new AppSettings(), new SshAgentHelper(Substitute.For<ILogger<SshAgentHelper>>(), null, new WslHelper(Substitute.For<ILogger<WslHelper>>())), new WslHelper(Substitute.For<ILogger<WslHelper>>())),
            Substitute.For<IStatusService>(),
            new FilePickerService(), repo);

        Assert.False(vm.IsNew);
        Assert.False(vm.IsOpen);
        Assert.Equal("My Repo", vm.Repository.Name);
        Assert.Single(vm.SourceDirectories);
        Assert.Equal(BackupMode.Scheduled, vm.Mode);
        Assert.Equal(ScheduleFrequency.Daily, vm.SelectedFrequency);
        Assert.Equal(3, vm.ScheduleHour);
    }

    // --- Save validation ---

    [Fact]
    public void Save_EmptyPath_SetsPathError()
    {
        var vm = CreateVm();
        vm.SaveCommand.Execute(null);

        Assert.NotNull(vm.PathError);
        Assert.False(vm.IsSaved);
    }

    [Fact]
    public void Save_SshWithoutHost_SetsPathError()
    {
        var vm = CreateVm();
        vm.Repository.IsLocal = false;
        vm.RepoPath = "/data/borg";
        vm.SshHost = ""; // missing host

        vm.SaveCommand.Execute(null);

        Assert.NotNull(vm.PathError);
        Assert.False(vm.IsSaved);
    }

    [Fact]
    public void Save_SshWithoutKeyPath_SetsSshKeyError()
    {
        var vm = CreateVm();
        vm.Repository.IsLocal = false;
        vm.RepoPath = "/data/borg";
        vm.SshHost = "example.com";
        vm.Repository.SshKeyPath = ""; // missing key

        vm.SaveCommand.Execute(null);

        Assert.NotNull(vm.SshKeyError);
        Assert.False(vm.IsSaved);
    }

    [Fact]
    public void Save_ValidLocal_SetsSaved()
    {
        var vm = RepositoryEditorViewModel.ForNew(
            new BorgServiceFactory(Substitute.For<ILoggerFactory>(), new AppSettings(), new SshAgentHelper(Substitute.For<ILogger<SshAgentHelper>>(), null, new WslHelper(Substitute.For<ILogger<WslHelper>>())), new WslHelper(Substitute.For<ILogger<WslHelper>>())),
            Substitute.For<IStatusService>(), new FilePickerService());
        vm.Repository.IsLocal = true;
        vm.Repository.Name = "Test";
        vm.RepoPath = "/data/borg";

        vm.SaveCommand.Execute(null);

        Assert.True(vm.IsSaved);
        Assert.Null(vm.PathError);
    }

    [Fact]
    public void Save_AppliesSourceDirectories()
    {
        var vm = RepositoryEditorViewModel.ForNew(
            new BorgServiceFactory(Substitute.For<ILoggerFactory>(), new AppSettings(), new SshAgentHelper(Substitute.For<ILogger<SshAgentHelper>>(), null, new WslHelper(Substitute.For<ILogger<WslHelper>>())), new WslHelper(Substitute.For<ILogger<WslHelper>>())),
            Substitute.For<IStatusService>(), new FilePickerService());
        vm.Repository.IsLocal = true;
        vm.Repository.Name = "Test";
        vm.RepoPath = "/data/borg";
        vm.SourceDirectories.Add("/home/user/docs");
        vm.SourceDirectories.Add("/home/user/photos");

        vm.SaveCommand.Execute(null);

        Assert.Equal(2, vm.Repository.SourceDirectories.Count);
    }

    [Fact]
    public void Save_AppliesSchedule()
    {
        var vm = RepositoryEditorViewModel.ForNew(
            new BorgServiceFactory(Substitute.For<ILoggerFactory>(), new AppSettings(), new SshAgentHelper(Substitute.For<ILogger<SshAgentHelper>>(), null, new WslHelper(Substitute.For<ILogger<WslHelper>>())), new WslHelper(Substitute.For<ILogger<WslHelper>>())),
            Substitute.For<IStatusService>(), new FilePickerService());
        vm.Repository.IsLocal = true;
        vm.Repository.Name = "Test";
        vm.RepoPath = "/data/borg";
        vm.Mode = BackupMode.Scheduled;
        vm.SelectedFrequency = ScheduleFrequency.Weekly;
        vm.ScheduleHour = 3;
        vm.SelectedDayOfWeek = DayOfWeek.Friday;

        vm.SaveCommand.Execute(null);

        Assert.Equal(BackupMode.Scheduled, vm.Repository.Mode);
        Assert.Equal(ScheduleFrequency.Weekly, vm.Repository.Schedule.Frequency);
        Assert.Equal(DayOfWeek.Friday, vm.Repository.Schedule.DayOfWeek);
    }

    [Fact]
    public void Save_WithNoName_SetsNameError()
    {
        var vm = RepositoryEditorViewModel.ForNew(
            new BorgServiceFactory(Substitute.For<ILoggerFactory>(), new AppSettings(), new SshAgentHelper(Substitute.For<ILogger<SshAgentHelper>>(), null, new WslHelper(Substitute.For<ILogger<WslHelper>>())), new WslHelper(Substitute.For<ILogger<WslHelper>>())),
            Substitute.For<IStatusService>(),
            new FilePickerService());
        vm.Repository.IsLocal = true;
        vm.RepoPath = "/data/my-backups";

        vm.SaveCommand.Execute(null);

        Assert.NotNull(vm.NameError);
        Assert.False(vm.IsSaved);
    }
}
