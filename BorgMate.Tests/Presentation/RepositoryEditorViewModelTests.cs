using BorgMate.Domain.Repositories;
using BorgMate.Presentation.Dialogs;
using RepositoryEditorViewModel = BorgMate.Presentation.ViewModels.RepositoryEditorViewModel;

namespace BorgMate.Tests.Presentation;

public class RepositoryEditorViewModelTests
{
    private static RepositoryEditorViewModel CreateVm() =>
        new(new FilePickerService());

    // --- Factory defaults ---

    [Fact]
    public void ForNew_SetsDefaults()
    {
        var vm = RepositoryEditorViewModel.ForNew(new FilePickerService());

        Assert.True(vm.IsNew);
        Assert.False(vm.IsOpen);
        Assert.True(vm.IsLocal);
        Assert.Equal(BorgEncryptionMode.RepokeyBlake2, vm.EncryptionMode);
        Assert.Null(vm.Repository); // Result is null until successful save
    }

    [Fact]
    public void ForOpen_SetsDefaults()
    {
        var vm = RepositoryEditorViewModel.ForOpen(new FilePickerService());

        Assert.True(vm.IsOpen);
        Assert.False(vm.IsNew);
        Assert.Null(vm.Repository);
    }

    [Fact]
    public void ForNew_HidesBorgVersionSelection_WhenBorg2SupportDisabled()
    {
        var vm = RepositoryEditorViewModel.ForNew(new FilePickerService());

        Assert.False(vm.IsBorg2SupportEnabled);
        Assert.False(vm.ShowBorgVersionSelection);
        Assert.Equal(BorgVersion.Borg1, vm.BorgVersion);
    }

    [Fact]
    public void ForNew_ShowsBorgVersionSelection_WhenBorg2SupportEnabled()
    {
        var vm = RepositoryEditorViewModel.ForNew(
            new FilePickerService(),
            isBorg2SupportEnabled: true);

        Assert.True(vm.IsBorg2SupportEnabled);
        Assert.True(vm.ShowBorgVersionSelection);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SetExcludeDatalessSupport_UpdatesAvailabilityAndDefault(bool isSupported)
    {
        var vm = RepositoryEditorViewModel.ForNew(new FilePickerService());

        vm.SetExcludeDatalessSupport(isSupported);

        Assert.Equal(isSupported, vm.IsExcludeDatalessSupported);
        Assert.Equal(isSupported, vm.ExcludeCloudFiles);
    }

    [Fact]
    public void ForEdit_LoadsAllFieldsFromRepo()
    {
        var repo = new BorgRepository
        {
            Name = "My Repo",
            Path = "user@host:/data/borg",
            IsLocal = false,
            SshKeyPath = "~/.ssh/id_ed25519",
            SshPort = 2222,
            BorgVersion = BorgVersion.Borg1,
            BorgRemotePath = "borg1",
            EncryptionMode = BorgEncryptionMode.Keyfile,
            RateLimit = 500,
            Mode = BackupMode.Scheduled,
        };
        repo.Schedule.Frequency = ScheduleFrequency.Daily;
        repo.Schedule.Hour = 3;
        repo.Schedule.Minute = 15;
        repo.Schedule.DayOfWeek = System.DayOfWeek.Friday;
        repo.SourceDirectories.Add("/home/user/docs");

        var vm = RepositoryEditorViewModel.ForEdit(new FilePickerService(), repo);

        Assert.False(vm.IsNew);
        Assert.False(vm.IsOpen);
        Assert.Equal("My Repo", vm.Name);
        Assert.False(vm.IsLocal);
        Assert.Equal("~/.ssh/id_ed25519", vm.SshKeyPath);
        Assert.Equal(2222, vm.SshPort);
        Assert.Equal(BorgVersion.Borg1, vm.BorgVersion);
        Assert.Equal("borg1", vm.BorgRemotePath);
        Assert.Equal(BorgEncryptionMode.Keyfile, vm.EncryptionMode);
        Assert.Equal(500, vm.RateLimit);
        Assert.Equal(BackupMode.Scheduled, vm.Mode);
        Assert.Equal(ScheduleFrequency.Daily, vm.SelectedFrequency);
        Assert.Equal(3, vm.ScheduleHour);
        Assert.Equal(15, vm.ScheduleMinute);
        Assert.Equal(System.DayOfWeek.Friday, vm.SelectedDayOfWeek);
        Assert.Single(vm.SourceDirectories);
        // Path decomposed into host/user/path VM fields
        Assert.Equal("host", vm.SshHost);
        Assert.Equal("user", vm.SshUser);
        Assert.Equal("/data/borg", vm.RepoPath);
    }

    // --- Duplicate ---

    [Fact]
    public void ForDuplicate_LoadsAllFieldsAndOverridesName()
    {
        var source = new BorgRepository
        {
            Name = "Original",
            Path = "user@host:/data/borg",
            IsLocal = false,
            SshKeyPath = "~/.ssh/id_ed25519",
            SshPort = 2222,
            BorgVersion = BorgVersion.Borg1,
            EncryptionMode = BorgEncryptionMode.RepokeyBlake2,
            RateLimit = 500,
            Mode = BackupMode.Scheduled,
            LastBackupAt = new DateTime(2026, 4, 1, 12, 0, 0),
        };
        source.Schedule.Frequency = ScheduleFrequency.Weekly;
        source.Schedule.Hour = 3;
        source.SourceDirectories.Add("/home/user/docs");
        source.PruneOptions.KeepDaily = 7;
        source.PruneOptions.KeepWeekly = 4;

        var vm = RepositoryEditorViewModel.ForDuplicate(
            new FilePickerService(), source, "Original (Copy)");

        Assert.True(vm.IsDuplicate);
        Assert.False(vm.IsNew);
        Assert.False(vm.IsOpen);
        Assert.Equal("Original (Copy)", vm.Name);
        Assert.Equal("host", vm.SshHost);
        Assert.Equal("user", vm.SshUser);
        Assert.Equal("/data/borg", vm.RepoPath);
        Assert.Equal(2222, vm.SshPort);
        Assert.Equal(BackupMode.Scheduled, vm.Mode);
        Assert.Equal(ScheduleFrequency.Weekly, vm.SelectedFrequency);
        Assert.Single(vm.SourceDirectories);
        Assert.True(vm.KeepDailyEnabled);
        Assert.Equal(7, vm.KeepDaily);
        Assert.True(vm.KeepWeeklyEnabled);
    }

    [Fact]
    public void ForDuplicate_ResetsBorgVersion_WhenBorg2SupportDisabled()
    {
        var source = new BorgRepository
        {
            Name = "Original",
            Path = "/data/borg",
            IsLocal = true,
            BorgVersion = BorgVersion.Borg2
        };

        var vm = RepositoryEditorViewModel.ForDuplicate(
            new FilePickerService(), source, "Original (Copy)");

        Assert.False(vm.ShowBorgVersionSelection);
        Assert.Equal(BorgVersion.Borg1, vm.BorgVersion);
    }

    [Fact]
    public void ForDuplicate_Save_NoDeps_ProducesFreshRepoCarryingLastBackupAt()
    {
        var lastBackup = new DateTime(2026, 4, 1, 12, 0, 0);
        var source = new BorgRepository
        {
            Name = "Original",
            Path = "/data/borg",
            IsLocal = true,
            LastBackupAt = lastBackup,
        };
        source.SourceDirectories.Add("/home/user/docs");

        var vm = RepositoryEditorViewModel.ForDuplicate(
            new FilePickerService(), source, "Original (Copy)");

        vm.SaveCommand.Execute(null);

        Assert.True(vm.IsSaved);
        Assert.NotNull(vm.Repository);
        Assert.NotSame(source, vm.Repository);
        Assert.Equal("Original (Copy)", vm.Repository!.Name);
        Assert.Equal("/data/borg", vm.Repository.Path);
        Assert.Equal(lastBackup, vm.Repository.LastBackupAt);
        // Nested objects must be independent instances.
        Assert.NotSame(source.Schedule, vm.Repository.Schedule);
        Assert.NotSame(source.PruneOptions, vm.Repository.PruneOptions);
        Assert.NotSame(source.SourceDirectories, vm.Repository.SourceDirectories);
    }

    [Fact]
    public void ForDuplicate_Save_DoesNotMutateSource()
    {
        var source = new BorgRepository
        {
            Name = "Original",
            Path = "/data/borg",
            IsLocal = true,
            RateLimit = 100,
        };
        source.SourceDirectories.Add("/home/user/docs");

        var vm = RepositoryEditorViewModel.ForDuplicate(
            new FilePickerService(), source, "Original (Copy)");

        vm.RateLimit = 999;
        vm.SourceDirectories.Add("/home/user/photos");
        vm.SaveCommand.Execute(null);

        Assert.True(vm.IsSaved);
        Assert.Equal("Original", source.Name);
        Assert.Equal(100, source.RateLimit);
        Assert.Single(source.SourceDirectories);
    }

    // --- Save validation ---

    [Fact]
    public void Save_EmptyPath_SetsPathError()
    {
        var vm = CreateVm();
        vm.SaveCommand.Execute(null);

        Assert.NotNull(vm.PathError);
        Assert.False(vm.IsSaved);
        Assert.Null(vm.Repository);
    }

    [Fact]
    public void Save_SshWithoutHost_SetsPathError()
    {
        var vm = CreateVm();
        vm.IsLocal = false;
        vm.RepoPath = "/data/borg";
        vm.SshHost = "";

        vm.SaveCommand.Execute(null);

        Assert.NotNull(vm.PathError);
        Assert.False(vm.IsSaved);
    }

    [Fact]
    public void Save_SshWithoutKeyPath_SetsSshKeyError()
    {
        var vm = CreateVm();
        vm.IsLocal = false;
        vm.RepoPath = "/data/borg";
        vm.SshHost = "example.com";
        vm.SshKeyPath = "";

        vm.SaveCommand.Execute(null);

        Assert.NotNull(vm.SshKeyError);
        Assert.False(vm.IsSaved);
    }

    [Fact]
    public void Save_WithNoName_SetsNameError()
    {
        var vm = RepositoryEditorViewModel.ForNew(new FilePickerService());
        vm.IsLocal = true;
        vm.RepoPath = "/data/my-backups";

        vm.SaveCommand.Execute(null);

        Assert.NotNull(vm.NameError);
        Assert.False(vm.IsSaved);
    }

    // --- Save success: New/Open build a fresh repo and expose it via vm.Repository ---

    [Fact]
    public void Save_ValidLocal_NoDeps_ExposesBuiltRepo()
    {
        var vm = RepositoryEditorViewModel.ForNew(new FilePickerService());
        vm.IsLocal = true;
        vm.Name = "Test";
        vm.RepoPath = "/data/borg";

        vm.SaveCommand.Execute(null);

        Assert.True(vm.IsSaved);
        Assert.Null(vm.PathError);
        Assert.NotNull(vm.Repository);
        Assert.Equal("Test", vm.Repository!.Name);
        Assert.Equal("/data/borg", vm.Repository.Path);
        Assert.True(vm.Repository.IsLocal);
        Assert.Equal(BorgRepositoryLocationKind.LocalPath, vm.Repository.LocationKind);
    }

    [Fact]
    public void Save_New_SshPathComposedFromFields()
    {
        var vm = RepositoryEditorViewModel.ForNew(new FilePickerService());
        vm.IsLocal = false;
        vm.Name = "Remote";
        vm.SshHost = "example.com";
        vm.SshUser = "borg";
        vm.RepoPath = "/srv/backups";
        vm.SshKeyPath = "/home/me/.ssh/id_ed25519";

        vm.SaveCommand.Execute(null);

        Assert.True(vm.IsSaved);
        Assert.NotNull(vm.Repository);
        Assert.Equal("borg@example.com:/srv/backups", vm.Repository!.Path);
        Assert.Equal(BorgRepositoryLocationKind.SshFields, vm.Repository.LocationKind);
    }

    // --- SMB location ---

    [Fact]
    public void IsSmbLocation_ClearsOtherLocationToggles()
    {
        var vm = CreateVm();

        vm.IsSmbLocation = true;
        Assert.False(vm.IsLocal);
        Assert.False(vm.IsSsh);
        Assert.False(vm.IsBorgUrlLocation);

        vm.IsLocal = true;
        Assert.False(vm.IsSmbLocation);

        vm.IsSsh = true;
        Assert.False(vm.IsSmbLocation);
        Assert.False(vm.IsLocal);
    }

    [Fact]
    public void ComposePath_Smb_BuildsSmbUrlFromFields()
    {
        var vm = CreateVm();
        vm.IsSmbLocation = true;
        vm.SmbHost = "nas.local";
        vm.SmbShare = "backups";
        vm.SmbUser = "bob";
        vm.RepoPath = "/laptop/repo";

        Assert.Equal("smb://bob@nas.local/backups/laptop/repo", vm.ComposePath());
    }

    [Fact]
    public void ComposePath_Smb_OmitsEmptyUserAndPath()
    {
        var vm = CreateVm();
        vm.IsSmbLocation = true;
        vm.SmbHost = "nas";
        vm.SmbShare = "backups";

        Assert.Equal("smb://nas/backups", vm.ComposePath());
    }

    [Fact]
    public void ForEdit_SmbRepo_DecomposesUrlIntoFields()
    {
        var repo = new BorgRepository
        {
            Name = "NAS",
            Path = "smb://bob@nas.local/backups/laptop",
            LocationKind = BorgRepositoryLocationKind.SmbShare,
            IsLocal = false
        };

        var vm = RepositoryEditorViewModel.ForEdit(new FilePickerService(), repo);

        Assert.True(vm.IsSmbLocation);
        Assert.False(vm.IsLocal);
        Assert.Equal("nas.local", vm.SmbHost);
        Assert.Equal("backups", vm.SmbShare);
        Assert.Equal("bob", vm.SmbUser);
        Assert.Equal("laptop", vm.RepoPath);
    }

    [Fact]
    public void Save_New_Smb_ProducesSmbShareRepo()
    {
        var vm = RepositoryEditorViewModel.ForNew(new FilePickerService());
        vm.Name = "NAS";
        vm.IsSmbLocation = true;
        vm.SmbHost = "nas.local";
        vm.SmbShare = "backups";
        vm.RepoPath = "repo";

        vm.SaveCommand.Execute(null);

        Assert.True(vm.IsSaved);
        Assert.NotNull(vm.Repository);
        Assert.Equal(BorgRepositoryLocationKind.SmbShare, vm.Repository!.LocationKind);
        Assert.Equal("smb://nas.local/backups/repo", vm.Repository.Path);
        Assert.False(vm.Repository.IsLocal);
    }

    [Fact]
    public void Save_Smb_WithoutHostOrShare_SetsPathError()
    {
        var vm = RepositoryEditorViewModel.ForNew(new FilePickerService());
        vm.Name = "NAS";
        vm.IsSmbLocation = true;
        vm.SmbHost = "nas.local";

        vm.SaveCommand.Execute(null);

        Assert.NotNull(vm.PathError);
        Assert.False(vm.IsSaved);
    }

    [Fact]
    public void Save_New_Borg2RawUrl_PreservesRawUrlLocation()
    {
        var vm = RepositoryEditorViewModel.ForNew(new FilePickerService());
        vm.Name = "Cloud";
        vm.BorgVersion = BorgVersion.Borg2;
        vm.IsBorgUrlLocation = true;
        vm.BorgUrl = "s3:bucket/path";
        vm.Borg2EncryptionMode = Borg2EncryptionMode.KeyfileAesOcb;

        vm.SaveCommand.Execute(null);

        Assert.True(vm.IsSaved);
        var repository = Assert.IsType<BorgMate.Presentation.ViewModels.RepositoryViewModel>(vm.Repository);
        Assert.Equal(BorgRepositoryLocationKind.BorgUrl, repository.LocationKind);
        Assert.Equal("s3:bucket/path", repository.Path);
        Assert.Equal("s3:bucket/path", repository.BorgUrl);
        Assert.Equal(Borg2EncryptionMode.KeyfileAesOcb, repository.Borg2EncryptionMode);
        Assert.False(repository.IsLocal);
    }

    [Fact]
    public void Save_BorgUrlWithBorg1_SetsPathError()
    {
        var vm = RepositoryEditorViewModel.ForNew(new FilePickerService());
        vm.Name = "Cloud";
        vm.IsBorgUrlLocation = true;
        vm.BorgUrl = "s3:bucket/path";

        vm.SaveCommand.Execute(null);

        Assert.False(vm.IsSaved);
        Assert.NotNull(vm.PathError);
    }

    [Fact]
    public void Save_BorgUrlWithEmbeddedSecret_SetsPathError()
    {
        var vm = RepositoryEditorViewModel.ForNew(new FilePickerService());
        vm.Name = "Cloud";
        vm.BorgVersion = BorgVersion.Borg2;
        vm.IsBorgUrlLocation = true;
        vm.BorgUrl = "ssh://user:pass@host.example.com//srv/borg";

        vm.SaveCommand.Execute(null);

        Assert.False(vm.IsSaved);
        Assert.NotNull(vm.PathError);
    }

    [Fact]
    public void ForEdit_LoadsBorg2RawUrlLocation()
    {
        var repo = new BorgRepository
        {
            Name = "Cloud",
            Path = "rest://repo.example.com/backups",
            BorgUrl = "rest://repo.example.com/backups",
            BorgVersion = BorgVersion.Borg2,
            LocationKind = BorgRepositoryLocationKind.BorgUrl
        };

        var vm = RepositoryEditorViewModel.ForEdit(new FilePickerService(), repo);

        Assert.True(vm.IsBorgUrlLocation);
        Assert.False(vm.IsLocal);
        Assert.False(vm.IsSsh);
        Assert.Equal("rest://repo.example.com/backups", vm.BorgUrl);
        Assert.Equal("", vm.RepoPath);
    }

    [Fact]
    public void Save_New_AppliesSourceDirectories()
    {
        var vm = RepositoryEditorViewModel.ForNew(new FilePickerService());
        vm.IsLocal = true;
        vm.Name = "Test";
        vm.RepoPath = "/data/borg";
        vm.SourceDirectories.Add("/home/user/docs");
        vm.SourceDirectories.Add("/home/user/photos");

        vm.SaveCommand.Execute(null);

        Assert.NotNull(vm.Repository);
        Assert.Equal(2, vm.Repository!.SourceDirectories.Count);
    }

    [Fact]
    public void Save_New_AppliesSchedule()
    {
        var vm = RepositoryEditorViewModel.ForNew(new FilePickerService());
        vm.IsLocal = true;
        vm.Name = "Test";
        vm.RepoPath = "/data/borg";
        vm.Mode = BackupMode.Scheduled;
        vm.SelectedFrequency = ScheduleFrequency.Weekly;
        vm.ScheduleHour = 3;
        vm.SelectedDayOfWeek = System.DayOfWeek.Friday;

        vm.SaveCommand.Execute(null);

        Assert.NotNull(vm.Repository);
        Assert.Equal(BackupMode.Scheduled, vm.Repository!.Mode);
        Assert.Equal(ScheduleFrequency.Weekly, vm.Repository.Schedule.Frequency);
        Assert.Equal(System.DayOfWeek.Friday, vm.Repository.Schedule.DayOfWeek);
    }

    // --- Save success: Edit applies VM state to the target in place ---

    [Fact]
    public void Save_Edit_NoDeps_AppliesToTargetRepoInPlace()
    {
        var target = new BorgRepository
        {
            Name = "Original",
            Path = "/orig",
            IsLocal = true,
            RateLimit = 100,
        };
        var vm = RepositoryEditorViewModel.ForEdit(new FilePickerService(), target);

        vm.Name = "Renamed";
        vm.RateLimit = 500;
        vm.Mode = BackupMode.Scheduled;

        vm.SaveCommand.Execute(null);

        Assert.True(vm.IsSaved);
        Assert.NotNull(vm.Repository);
        Assert.Same(target, vm.Repository.Model); // The wrapper still points at the same persisted model.
        Assert.Equal("Renamed", target.Name);
        Assert.Equal(500, target.RateLimit);
        Assert.Equal(BackupMode.Scheduled, target.Mode);
    }

    [Fact]
    public void Save_Edit_ValidationFailure_DoesNotTouchTarget()
    {
        var target = new BorgRepository { Name = "Original", Path = "/orig", IsLocal = true };
        var vm = RepositoryEditorViewModel.ForEdit(new FilePickerService(), target);

        // Clear the name to force validation failure.
        vm.Name = "";
        vm.SaveCommand.Execute(null);

        Assert.False(vm.IsSaved);
        Assert.NotNull(vm.NameError);
        // Target must be untouched — Save bailed out of validation before CommitResult.
        Assert.Equal("Original", target.Name);
        Assert.Null(vm.Repository);
    }

    [Fact]
    public void Save_Edit_ReachabilityChangeWithoutBorgDeps_StillAppliesAtCommit()
    {
        // Without borg deps (test path), even a reachability-triggering change
        // falls through to CommitResult via the null-deps short-circuit.
        var target = new BorgRepository
        {
            Name = "Remote",
            Path = "user@host:/data",
            IsLocal = false,
            SshKeyPath = "/home/me/.ssh/key",
            SshPort = 2222,
        };
        var vm = RepositoryEditorViewModel.ForEdit(new FilePickerService(), target);

        vm.SshPort = 9999;
        vm.SaveCommand.Execute(null);

        Assert.True(vm.IsSaved);
        Assert.Equal(9999, target.SshPort);
    }

    // --- Target is untouched during editing: VM mutations don't leak ---

    [Fact]
    public void Editing_VmFields_DoesNotTouchTargetUntilSave()
    {
        var target = new BorgRepository
        {
            Name = "Untouched",
            Path = "user@host:/data",
            IsLocal = false,
            SshKeyPath = "/home/me/.ssh/key",
            SshPort = 2222,
            RateLimit = 100,
        };
        var vm = RepositoryEditorViewModel.ForEdit(new FilePickerService(), target);

        // Mutate every VM field the AXAML can touch.
        vm.Name = "Edited";
        vm.SshHost = "new-host.example.com";
        vm.SshUser = "newuser";
        vm.RepoPath = "/new/path";
        vm.SshKeyPath = "/home/me/.ssh/other";
        vm.SshPort = 9999;
        vm.BorgRemotePath = "/usr/local/bin/borg";
        vm.RateLimit = 999;
        vm.IsLocal = true;
        vm.BorgVersion = BorgVersion.Borg2;
        vm.EncryptionMode = BorgEncryptionMode.None;
        vm.Mode = BackupMode.Scheduled;
        vm.ScheduleHour = 20;
        vm.SourceDirectories.Add("/some/new/dir");

        // Target must remain byte-identical to its pre-edit state.
        Assert.Equal("Untouched", target.Name);
        Assert.Equal("user@host:/data", target.Path);
        Assert.False(target.IsLocal);
        Assert.Equal("/home/me/.ssh/key", target.SshKeyPath);
        Assert.Equal(2222, target.SshPort);
        Assert.Equal(100, target.RateLimit);
        Assert.Empty(target.SourceDirectories);
    }

    [Fact]
    public void EditingCancelled_NeverCallingSave_TargetUntouched()
    {
        var target = new BorgRepository
        {
            Name = "Untouched",
            Path = "/orig",
            IsLocal = true,
            RateLimit = 100,
        };
        var vm = RepositoryEditorViewModel.ForEdit(new FilePickerService(), target);

        vm.Name = "Edited";
        vm.RateLimit = 999;
        // User closes the dialog via Cancel — Save is never called. IsSaved stays false.

        Assert.False(vm.IsSaved);
        Assert.Equal("Untouched", target.Name);
        Assert.Equal(100, target.RateLimit);
    }

    // --- Reachability check: Edit skips borg info when nothing relevant changed ---

    private static BorgRepository MakeSshRepo() => new()
    {
        Name = "Remote",
        Path = "user@host.example.com:/data/borg",
        IsLocal = false,
        SshKeyPath = "/home/me/.ssh/id_ed25519",
        SshPort = 2222,
        BorgRemotePath = "borg1",
        BorgVersion = BorgVersion.Borg1,
    };

    // NeedsReachabilityCheck removed — Save always verifies with borg.

    // --- Save/verify gating ---

    [Fact]
    public void IsVerifying_DisablesSaveCommand()
    {
        var vm = CreateVm();
        Assert.True(vm.SaveCommand.CanExecute(null));

        vm.IsVerifying = true;
        Assert.False(vm.SaveCommand.CanExecute(null));

        vm.IsVerifying = false;
        Assert.True(vm.SaveCommand.CanExecute(null));
    }

    // --- VerifyMessage: single-line display logic ---

    [Fact]
    public void VerifyMessage_WhileVerifying_ShowsStatus()
    {
        var vm = CreateVm();
        vm.IsVerifying = true;
        vm.VerifyStatus = "Verifying repository...";
        vm.VerifyError = "This should be ignored while verifying";

        Assert.Equal("Verifying repository...", vm.VerifyMessage);
        Assert.False(vm.VerifyMessageIsError);
        Assert.True(vm.HasVerifyMessage);
    }

    [Fact]
    public void VerifyMessage_ErrorAfterVerify_ShowsFirstLineOnly()
    {
        var vm = CreateVm();
        vm.IsVerifying = false;
        vm.VerifyError = "Connection refused\nssh: could not resolve hostname\n  stack trace...";

        Assert.Equal("Connection refused", vm.VerifyMessage);
        Assert.True(vm.VerifyMessageIsError);
        Assert.True(vm.HasVerifyMessage);
    }

    [Fact]
    public void VerifyMessage_SingleLineError_ShowsAsIs()
    {
        var vm = CreateVm();
        vm.VerifyError = "Wrong passphrase";

        Assert.Equal("Wrong passphrase", vm.VerifyMessage);
        Assert.True(vm.VerifyMessageIsError);
    }

    [Fact]
    public void VerifyMessage_ErrorWithCrLf_TrimsAtFirstLineBreak()
    {
        var vm = CreateVm();
        vm.VerifyError = "Line one\r\nLine two";

        Assert.Equal("Line one", vm.VerifyMessage);
    }

    [Fact]
    public void VerifyMessage_NoErrorNoStatus_Empty()
    {
        var vm = CreateVm();
        Assert.False(vm.HasVerifyMessage);
        Assert.Null(vm.VerifyMessage);
    }

    [Fact]
    public void VerifyMessage_ChangingIsVerifying_NotifiesComputedProperties()
    {
        var vm = CreateVm();
        vm.VerifyError = "Connection refused\nmore details";
        vm.IsVerifying = true;

        Assert.False(vm.VerifyMessageIsError);

        vm.IsVerifying = false;
        Assert.Equal("Connection refused", vm.VerifyMessage);
        Assert.True(vm.VerifyMessageIsError);
    }
}
