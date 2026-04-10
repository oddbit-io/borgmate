using System;
using System.IO;
using BorgMate.Models;
using BorgMate.Services.Config;
using BorgMate.Services.Borg;
using BorgMate.Services.Journal;
using BorgMate.Services.Keychain;
using BorgMate.Services.Mocks;
using BorgMate.Services.Power;
using BorgMate.Services.Queue;
using BorgMate.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

namespace BorgMate.Services;

public static class ServiceCollectionExtensions
{
    public static ServiceProvider ConfigureBorgMateServices(this ServiceCollection services)
    {
        var configService = new ConfigService();
        var config = configService.Load();
        var settings = config.Settings;

        var logConfig = new LoggerConfiguration().MinimumLevel.Debug();

        if (settings.LoggingEnabled)
        {
            var logDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "BorgMate", "logs");
            logConfig.WriteTo.File(
                Path.Combine(logDirectory, "borg-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: settings.LogRetention?.CutoffDate() is { } c ? (int)(DateTime.Now - c).TotalDays : null,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}");
        }

        var serilogLogger = logConfig.CreateLogger();

        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.AddSerilog(serilogLogger, dispose: true);
        });

        services.AddSingleton(settings);
        services.AddSingleton<BorgServiceFactory>();
#if DEBUG
        if (Program.DemoMode)
        {
            services.AddSingleton<IConfigService, MockConfigService>();
            services.AddSingleton<IJournalService, MockJournalService>();
            services.AddSingleton<ISchedulerService, MockSchedulerService>();
        }
        else
#endif
        {
            services.AddSingleton<IConfigService, ConfigService>();
            services.AddSingleton<IJournalService, Journal.JournalService>();
            services.AddSingleton<ISchedulerService, SchedulerService>();
        }
        services.AddSingleton<StatusService>();
        services.AddSingleton<IStatusService>(sp => sp.GetRequiredService<StatusService>());
        if (OperatingSystem.IsMacOS())
            services.AddSingleton<IFilePickerService, MacOsFilePickerService>();
        else
            services.AddSingleton<IFilePickerService, FilePickerService>();
        services.AddSingleton<BorgCacheService>();
        services.AddSingleton<JobQueueService>(sp =>
            new JobQueueService(sp.GetRequiredService<ILogger<JobQueueService>>()));
        if (OperatingSystem.IsMacOS())
        {
            services.AddSingleton<IKeychainService, MacOsKeychainService>();
            services.AddSingleton<IAutoStartService, MacOsAutoStartService>();
            services.AddSingleton<INotificationService, MacOsNotificationService>();
            services.AddSingleton<ISleepInhibitor, MacOsSleepInhibitor>();
        }
        else if (OperatingSystem.IsLinux())
        {
            services.AddSingleton<IKeychainService, LinuxKeychainService>();
            services.AddSingleton<IAutoStartService, LinuxAutoStartService>();
            services.AddSingleton<INotificationService, LinuxNotificationService>();
            services.AddSingleton<ISleepInhibitor, LinuxSleepInhibitor>();
        }
        else if (OperatingSystem.IsWindows())
        {
#pragma warning disable CA1416 // Platform guard via OperatingSystem.IsWindows()
            services.AddSingleton<IKeychainService, WindowsKeychainService>();
            services.AddSingleton<IAutoStartService, WindowsAutoStartService>();
            services.AddSingleton<INotificationService, WindowsNotificationService>();
            services.AddSingleton<ISleepInhibitor, WindowsSleepInhibitor>();
#pragma warning restore CA1416
        }
        services.AddSingleton<WslHelper>();
        services.AddSingleton<UI.PassphrasePrompt>();
        services.AddSingleton<SshAgentHelper>();
        services.AddSingleton<DirectorySizeCalculator>();
        services.AddSingleton<BorgOperationRunner>();
        services.AddSingleton<UpdateService>();
        services.AddSingleton<Notifications.NotificationService>();
        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<RepositoryListViewModel>();
        services.AddTransient<ArchiveListViewModel>();
        services.AddTransient<NotificationsViewModel>();

        var provider = services.BuildServiceProvider();

        // Initialize remaining static helper
        Strings.Initialize(settings);

        return provider;
    }
}
