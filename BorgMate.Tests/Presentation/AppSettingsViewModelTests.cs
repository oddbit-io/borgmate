using BorgMate.Domain.Settings;
using BorgMate.Infrastructure.Borg;
using BorgMate.Infrastructure.Platform;
using BorgMate.Persistence.Configuration;
using NSubstitute;
using AppSettingsViewModel = BorgMate.Presentation.ViewModels.AppSettingsViewModel;

namespace BorgMate.Tests.Presentation;

public class AppSettingsViewModelTests
{
    private readonly AppSettings _settings = new();
    private readonly ISettingsStore _settingsStore = Substitute.For<ISettingsStore>();
    private readonly IAutoStartService _autoStart = Substitute.For<IAutoStartService>();
    private readonly IWakeScheduler _wakeScheduler = Substitute.For<IWakeScheduler>();

    private AppSettingsViewModel CreateVm() => new(_settings, _settingsStore, _autoStart, _wakeScheduler);

    [Fact]
    public void Reload_PopulatesFromSettings()
    {
        _settings.Theme = AppTheme.Dark;
        _settings.CheckForUpdates = false;
        _settings.ShowNotifications = false;
        _settings.ExperimentalBorg2Support = true;
        _settings.StartMinimized = true;
        _settings.WakeForScheduledBackups = true;
        _settings.ScheduledBackupsOnlyOnAcPower = false;
        _settings.LoggingEnabled = false;
        _settings.LogLevel = AppLogLevel.Warning;
        _settings.BinaryUnits = false;
        _settings.SshKeepAliveInterval = 10;
        _settings.SshKeepAliveCountMax = 5;
        _settings.DefaultExtractOptions = new BorgExtractOptions
        {
            NoFlags = false,
            NoAcls = false,
            NoXattrs = true,
            Sparse = true
        };

        var vm = CreateVm();

        Assert.Equal(AppTheme.Dark, vm.SelectedTheme);
        Assert.False(vm.CheckForUpdates);
        Assert.False(vm.ShowNotifications);
        Assert.True(vm.ExperimentalBorg2Support);
        Assert.True(vm.StartMinimized);
        Assert.True(vm.WakeForScheduledBackups);
        Assert.False(vm.ScheduledBackupsOnlyOnAcPower);
        Assert.False(vm.LoggingEnabled);
        Assert.Equal(AppLogLevel.Warning, vm.LogLevel);
        Assert.False(vm.BinaryUnits);
        Assert.Equal(10, vm.SshKeepAliveInterval);
        Assert.Equal(5, vm.SshKeepAliveCountMax);
        Assert.False(vm.NoFlags);
        Assert.False(vm.NoAcls);
        Assert.True(vm.NoXattrs);
        Assert.True(vm.Sparse);
    }

    [Fact]
    public void Save_WritesBackToSettings()
    {
        var vm = CreateVm();
        vm.SelectedTheme = AppTheme.Light;
        vm.CheckForUpdates = false;
        vm.ShowNotifications = false;
        vm.ExperimentalBorg2Support = true;
        vm.StartMinimized = true;
        vm.WakeForScheduledBackups = true;
        vm.ScheduledBackupsOnlyOnAcPower = false;
        vm.BinaryUnits = false;
        vm.SshKeepAliveInterval = 15;
        vm.SshKeepAliveCountMax = 8;
        vm.LogLevel = AppLogLevel.Error;
        vm.NoFlags = false;
        vm.NoAcls = true;
        vm.NoXattrs = false;
        vm.Sparse = true;

        vm.SaveCommand.Execute(null);

        Assert.Equal(AppTheme.Light, _settings.Theme);
        Assert.False(_settings.CheckForUpdates);
        Assert.False(_settings.ShowNotifications);
        Assert.True(_settings.ExperimentalBorg2Support);
        Assert.True(_settings.StartMinimized);
        Assert.True(_settings.WakeForScheduledBackups);
        Assert.False(_settings.ScheduledBackupsOnlyOnAcPower);
        Assert.False(_settings.BinaryUnits);
        Assert.Equal(15, _settings.SshKeepAliveInterval);
        Assert.Equal(8, _settings.SshKeepAliveCountMax);
        Assert.Equal(AppLogLevel.Error, _settings.LogLevel);
        Assert.False(_settings.DefaultExtractOptions.NoFlags);
        Assert.True(_settings.DefaultExtractOptions.NoAcls);
        Assert.False(_settings.DefaultExtractOptions.NoXattrs);
        Assert.True(_settings.DefaultExtractOptions.Sparse);
    }

    [Fact]
    public void Save_SetsIsSaved()
    {
        var vm = CreateVm();
        Assert.False(vm.IsSaved);

        vm.SaveCommand.Execute(null);

        Assert.True(vm.IsSaved);
    }

    [Fact]
    public void Save_RequestsConfigSave()
    {
        var vm = CreateVm();
        vm.SaveCommand.Execute(null);

        _settingsStore.Received(1).RequestSave();
    }

    [Fact]
    public void Save_SetsAutoStart()
    {
        var vm = CreateVm();
        vm.StartAtLogin = true;
        vm.SaveCommand.Execute(null);

        _autoStart.Received(1).SetEnabled(true);
    }

    [Fact]
    public void Save_EmptyBorg1Path_SetsNull()
    {
        _settings.BorgBinaryPath = "/custom/borg";
        var vm = CreateVm();

        vm.Borg1BinaryPath = "";
        vm.SaveCommand.Execute(null);

        Assert.Null(_settings.BorgBinaryPath);
        Assert.Null(_settings.Borg1BinaryPath);
    }

    [Fact]
    public void Save_CustomBorgPaths_Preserved()
    {
        var vm = CreateVm();
        vm.Borg1BinaryPath = "/usr/local/bin/borg";
        vm.Borg2BinaryPath = "/usr/local/bin/borg2";
        vm.SaveCommand.Execute(null);

        Assert.Null(_settings.BorgBinaryPath);
        Assert.Equal("/usr/local/bin/borg", _settings.Borg1BinaryPath);
        Assert.Equal("/usr/local/bin/borg2", _settings.Borg2BinaryPath);
    }

    [Fact]
    public void DetectedBorg2PathDisplay_NotDetected_WhenOnlyFallbackExists()
    {
        var settings = new AppSettings(
            new BorgPathDetectionResult("borg", "borg"),
            new BorgPathDetectionResult("borg2", null));
        var vm = new AppSettingsViewModel(settings, _settingsStore, _autoStart, _wakeScheduler);

        Assert.Equal("borg2", vm.DetectedBorg2Path);
        Assert.Equal("Auto-detected: not found", vm.DetectedBorg2PathDisplay);
    }

    [Fact]
    public void WakeSchedulerStatusChanged_RaisesMessagePropertyChanged()
    {
        _wakeScheduler.IsSupported.Returns(true);
        _wakeScheduler.LastError.Returns("boom");
        var vm = CreateVm();
        vm.DispatchToUi = action => action();
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        _wakeScheduler.StatusChanged += Raise.Event<Action>();

        Assert.Contains(nameof(vm.WakeSchedulerMessage), raised);
        Assert.Contains(nameof(vm.HasWakeSchedulerMessage), raised);
    }

    [Fact]
    public void Detach_StopsWakeStatusUpdates()
    {
        var vm = CreateVm();
        vm.DispatchToUi = action => action();
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.Detach();
        _wakeScheduler.StatusChanged += Raise.Event<Action>();

        Assert.DoesNotContain(nameof(vm.WakeSchedulerMessage), raised);
    }

    [Fact]
    public void Reload_RetentionDisabled_WhenNull()
    {
        _settings.LogRetention = null;
        _settings.JournalRetention = null;

        var vm = CreateVm();

        Assert.False(vm.IsLogRetentionEnabled);
        Assert.False(vm.IsJournalRetentionEnabled);
    }

    [Fact]
    public void Save_RetentionDisabled_SetsNull()
    {
        var vm = CreateVm();
        vm.IsLogRetentionEnabled = false;
        vm.IsJournalRetentionEnabled = false;
        vm.SaveCommand.Execute(null);

        Assert.Null(_settings.LogRetention);
        Assert.Null(_settings.JournalRetention);
    }

    [Fact]
    public void Save_RetentionEnabled_SetsValue()
    {
        var vm = CreateVm();
        vm.IsLogRetentionEnabled = true;
        vm.LogRetention = RetentionPeriod.OneMonth;
        vm.IsJournalRetentionEnabled = true;
        vm.JournalRetention = RetentionPeriod.OneYear;
        vm.SaveCommand.Execute(null);

        Assert.Equal(RetentionPeriod.OneMonth, _settings.LogRetention);
        Assert.Equal(RetentionPeriod.OneYear, _settings.JournalRetention);
    }
}
