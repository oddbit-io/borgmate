using BorgMate.Models;
using BorgMate.Services.AutoStart;
using BorgMate.Services.Config;
using BorgMate.ViewModels;
using NSubstitute;

namespace BorgMate.Tests;

public class AppSettingsViewModelTests
{
    private readonly AppSettings _settings = new();
    private readonly IConfigService _configService = Substitute.For<IConfigService>();
    private readonly IAutoStartService _autoStart = Substitute.For<IAutoStartService>();

    private AppSettingsViewModel CreateVm() => new(_settings, _configService, _autoStart);

    [Fact]
    public void Reload_PopulatesFromSettings()
    {
        _settings.Theme = AppTheme.Dark;
        _settings.CheckForUpdates = false;
        _settings.ShowNotifications = false;
        _settings.StartMinimized = true;
        _settings.LoggingEnabled = false;
        _settings.LogLevel = AppLogLevel.Warning;
        _settings.BinaryUnits = false;
        _settings.SshKeepAliveInterval = 10;
        _settings.SshKeepAliveCountMax = 5;

        var vm = CreateVm();

        Assert.Equal(AppTheme.Dark, vm.SelectedTheme);
        Assert.False(vm.CheckForUpdates);
        Assert.False(vm.ShowNotifications);
        Assert.True(vm.StartMinimized);
        Assert.False(vm.LoggingEnabled);
        Assert.Equal(AppLogLevel.Warning, vm.LogLevel);
        Assert.False(vm.BinaryUnits);
        Assert.Equal(10, vm.SshKeepAliveInterval);
        Assert.Equal(5, vm.SshKeepAliveCountMax);
    }

    [Fact]
    public void Save_WritesBackToSettings()
    {
        var vm = CreateVm();
        vm.SelectedTheme = AppTheme.Light;
        vm.CheckForUpdates = false;
        vm.ShowNotifications = false;
        vm.StartMinimized = true;
        vm.BinaryUnits = false;
        vm.SshKeepAliveInterval = 15;
        vm.SshKeepAliveCountMax = 8;
        vm.LogLevel = AppLogLevel.Error;

        vm.SaveCommand.Execute(null);

        Assert.Equal(AppTheme.Light, _settings.Theme);
        Assert.False(_settings.CheckForUpdates);
        Assert.False(_settings.ShowNotifications);
        Assert.True(_settings.StartMinimized);
        Assert.False(_settings.BinaryUnits);
        Assert.Equal(15, _settings.SshKeepAliveInterval);
        Assert.Equal(8, _settings.SshKeepAliveCountMax);
        Assert.Equal(AppLogLevel.Error, _settings.LogLevel);
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

        _configService.Received(1).RequestSave();
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
    public void Save_EmptyBorgPath_SetsNull()
    {
        _settings.BorgBinaryPath = "/custom/borg";
        var vm = CreateVm();

        vm.BorgBinaryPath = "";
        vm.SaveCommand.Execute(null);

        Assert.Null(_settings.BorgBinaryPath);
    }

    [Fact]
    public void Save_CustomBorgPath_Preserved()
    {
        var vm = CreateVm();
        vm.BorgBinaryPath = "/usr/local/bin/borg";
        vm.SaveCommand.Execute(null);

        Assert.Equal("/usr/local/bin/borg", _settings.BorgBinaryPath);
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
