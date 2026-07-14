using System;
using System.IO;
using BorgMate.Domain.Settings;
using BorgMate.Infrastructure.Borg;
using BorgMate.Infrastructure.Notifications;
using BorgMate.Infrastructure.Platform;
using BorgMate.Infrastructure.Updates;
using BorgMate.Persistence.Configuration;
using BorgMate.Persistence.Journal;
using BorgMate.Persistence.Keychain;
using BorgMate.Presentation.Dialogs;
using BorgMate.Presentation.State;
using BorgMate.Presentation.ViewModels;
using BorgMate.Runtime.Jobs;
using BorgMate.Runtime.Operations;
using BorgMate.Runtime.Orchestration;
using BorgMate.Runtime.Scheduling;
using BorgMate.Runtime.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using WslHelper = BorgMate.Infrastructure.Platform.WslHelper;

namespace BorgMate.Composition;

public static class ServiceCollectionExtensions
{
    public static ServiceProvider ConfigureBorgMateServices(this ServiceCollection services)
    {
#if DEBUG
        var configStore = new JsonAppConfigStore(!Program.DemoMode);
#else
        var configStore = new JsonAppConfigStore();
#endif
        var config = configStore.Load();
        var settings = config.Settings;

        var minimumLevel = settings.LogLevel switch
        {
            AppLogLevel.Debug => LogEventLevel.Debug,
            AppLogLevel.Info => LogEventLevel.Information,
            AppLogLevel.Warning => LogEventLevel.Warning,
            AppLogLevel.Error => LogEventLevel.Error,
            _ => LogEventLevel.Information
        };
        var logConfig = new LoggerConfiguration().MinimumLevel.Is(minimumLevel);

        if (settings.LoggingEnabled)
        {
            var logDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "BorgMate", "logs");
            logConfig.WriteTo.File(
                Path.Combine(logDirectory, "borg-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: settings.LogRetention?.CutoffDate() is { } c
                    ? (int)(DateTime.Now - c).TotalDays
                    : null,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}");
        }

        var serilogLogger = logConfig.CreateLogger();

        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.AddSerilog(serilogLogger, true);
        });

        services.AddSingleton(settings);
        services.AddSingleton<SshRepositoryTransport>();
        services.AddSingleton<BorgServiceFactory>();
        services.AddSingleton(configStore);
        services.AddSingleton<ISettingsStore>(sp => sp.GetRequiredService<JsonAppConfigStore>());
        services.AddSingleton<IRepositoryConfigStore>(sp => sp.GetRequiredService<JsonAppConfigStore>());
#if DEBUG
        if (Program.DemoMode)
        {
            services.AddSingleton<IJournalStore, MockJournalStore>();
            services.AddSingleton<IBackupScheduler, MockBackupScheduler>();
        }
        else
#endif
        {
            services.AddSingleton<IJournalStore, SqliteJournalStore>();
            services.AddSingleton<IBackupScheduler, BackupScheduler>();
        }

        services.AddSingleton<UpdateStatusState>();
        services.AddSingleton<IFilePickerService, FilePickerService>();
        services.AddSingleton<BorgCacheService>();
        services.AddSingleton<BorgJobQueue>(sp =>
            new BorgJobQueue(sp.GetRequiredService<ILogger<BorgJobQueue>>()));
        services.AddSingleton<IJobController>(sp => sp.GetRequiredService<BorgJobQueue>());
        services.AddSingleton<RepositoryPresentationState>();
        if (OperatingSystem.IsMacOS())
        {
            services.AddSingleton<ICredentialStore, MacOsKeychainService>();
            services.AddSingleton<INetworkShareMounter, MacOsNetworkShareMounter>();
            services.AddSingleton<IAutoStartService, MacOsAutoStartService>();
            services.AddSingleton<INotificationService, MacOsNotificationService>();
            services.AddSingleton<ISleepInhibitor, MacOsSleepInhibitor>();
            services.AddSingleton<IWakeScheduler, MacOsWakeScheduler>();
            services.AddSingleton<IPowerSource, MacOsPowerSource>();
        }
        else if (OperatingSystem.IsLinux())
        {
            services.AddSingleton<ICredentialStore, LinuxKeychainService>();
            services.AddSingleton<INetworkShareMounter, LinuxNetworkShareMounter>();
            services.AddSingleton<IAutoStartService, LinuxAutoStartService>();
            services.AddSingleton<INotificationService, LinuxNotificationService>();
            services.AddSingleton<ISleepInhibitor, LinuxSleepInhibitor>();
            services.AddSingleton<IWakeScheduler, LinuxWakeScheduler>();
            services.AddSingleton<IPowerSource, LinuxPowerSource>();
        }
        else if (OperatingSystem.IsWindows())
        {
#pragma warning disable CA1416 // Platform guard via OperatingSystem.IsWindows()
            services.AddSingleton<ICredentialStore, WindowsKeychainService>();
            services.AddSingleton<INetworkShareMounter, WindowsWslNetworkShareMounter>();
            services.AddSingleton<IAutoStartService, WindowsAutoStartService>();
            services.AddSingleton<INotificationService, WindowsNotificationService>();
            services.AddSingleton<ISleepInhibitor, WindowsSleepInhibitor>();
            services.AddSingleton<IWakeScheduler, WindowsWakeScheduler>();
            services.AddSingleton<IPowerSource, WindowsPowerSource>();
#pragma warning restore CA1416
        }

        services.AddSingleton<IWslAvailabilityProbe>(sp =>
            new WslAvailabilityProbe(sp.GetRequiredService<ILogger<WslAvailabilityProbe>>()));
        services.AddSingleton<WslHelper>(sp =>
            new WslHelper(
                sp.GetRequiredService<ILogger<WslHelper>>(),
                sp.GetRequiredService<IWslAvailabilityProbe>(),
                WslHelper.IsRequired));
        services.AddSingleton<PromptingPassphraseProvider>();
        services.AddSingleton<IPassphraseProvider>(sp => sp.GetRequiredService<PromptingPassphraseProvider>());
        services.AddSingleton<TransientRetryPolicy>();
        services.AddSingleton<BorgOperationOrchestrator>();
        services.AddSingleton<RepositoryStorageQuery>();
        services.AddSingleton<IRepositoryOperationRunner, RepositoryOperationRunner>();
        services.AddSingleton<IArchiveOperationRunner, ArchiveOperationRunner>();
        services.AddSingleton<IRepositoryVerificationOperationRunner, RepositoryVerificationOperationRunner>();
        services.AddSingleton<SshAgentHelper>();
        services.AddSingleton<DirectorySizeCalculator>();
        services.AddSingleton<UpdateService>();
        services.AddSingleton<NotificationService>();
        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<RepositoriesPageViewModel>();
        services.AddTransient<NotificationsViewModel>();

        var provider = services.BuildServiceProvider();

        // Initialize remaining static helper
        Strings.Initialize(settings);

        return provider;
    }
}
