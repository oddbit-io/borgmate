using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;
using BorgMate.Composition;
using BorgMate.Domain.Settings;
using BorgMate.Infrastructure.Borg;
using BorgMate.Infrastructure.Platform;
using BorgMate.Infrastructure.Updates;
using BorgMate.Persistence.Configuration;
using BorgMate.Persistence.Journal;
using BorgMate.Presentation.Dialogs;
using BorgMate.Presentation.State;
using BorgMate.Runtime.Jobs;
using BorgMate.Runtime.Scheduling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using AboutWindow = BorgMate.Presentation.Views.AboutWindow;
using MainWindow = BorgMate.Presentation.Views.MainWindow;
using MainWindowViewModel = BorgMate.Presentation.ViewModels.MainWindowViewModel;
using UpdateChangelogWindow = BorgMate.Presentation.Views.UpdateChangelogWindow;

namespace BorgMate;

public class App : Application
{
    private static App? _instance;
    private BorgCacheService _cache = null!;
    private bool _hadRunningJobs;
    private volatile bool _isShuttingDown;
    private BorgJobQueue _jobQueue = null!;
    private ILogger<App> _logger = null!;
    private MainWindowViewModel? _mainVm;
    private MainWindow? _mainWindow;
    private AppSettings _settings = null!;
    private bool _shutdownPrepared;
    private Task? _shutdownPreparationTask;
    private PosixSignalRegistration? _sigHupRegistration;
    private PosixSignalRegistration? _sigIntRegistration;
    private PosixSignalRegistration? _sigTermRegistration;
    private bool _sleepInhibited;
    private ISleepInhibitor _sleepInhibitor = null!;
    private UpdateStatusState _status = null!;
    private TrayIcon? _trayIcon;
    private UpdateService _updateService = null!;
    public static IServiceProvider Services { get; private set; } = null!;
    internal static AppBuildInfo BuildInfo { get; } = AppBuildInfo.FromAssembly(Assembly.GetExecutingAssembly());

    public override void Initialize()
    {
        if (!Design.IsDesignMode)
            Strings.ApplyLanguageCode(new JsonAppConfigStore().Load().Settings.Language);

        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        _instance = this;
        Services = ConfigureServices();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _settings = Services.GetRequiredService<AppSettings>();

            // AC-only backups gate: pmset can't condition the wake itself, so a scheduled-wake
            // launch on battery exits right away and lets the machine go back to sleep.
            // Windows/Linux don't need this — the task/service is conditioned by the OS.
            if (Program.ScheduledWakeMode && OperatingSystem.IsMacOS() &&
                _settings.ScheduledBackupsOnlyOnAcPower &&
                Task.Run(Services.GetRequiredService<IPowerSource>().IsOnBatteryAsync).GetAwaiter().GetResult())
            {
                _shutdownPrepared = true;
                desktop.Shutdown();
                base.OnFrameworkInitializationCompleted();
                return;
            }

            _jobQueue = Services.GetRequiredService<BorgJobQueue>();
            _cache = Services.GetRequiredService<BorgCacheService>();
            _status = Services.GetRequiredService<UpdateStatusState>();
            _updateService = Services.GetRequiredService<UpdateService>();
            _sleepInhibitor = Services.GetRequiredService<ISleepInhibitor>();
            _mainVm = Services.GetRequiredService<MainWindowViewModel>();
            _logger = Services.GetRequiredService<ILogger<App>>();

            _logger.LogInformation("BorgMate v{Version} starting on {OS} {Arch}",
                BuildInfo.DisplayVersion, RuntimeInformation.OSDescription,
                RuntimeInformation.OSArchitecture);
            var journal = Services.GetRequiredService<IJournalStore>();
            journal.Load();

            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            desktop.ShutdownRequested += async (_, e) =>
            {
                if (_shutdownPrepared)
                    return;

                e.Cancel = true;
                await PrepareShutdownAsync();
                desktop.TryShutdown();
            };

            // Handle POSIX shutdown signals so session managers (KDE ksmserver,
            // GNOME Shell, systemd-logind) can cleanly stop BorgMate. Without
            // this, the tray icon keeps the process alive after WM_DELETE_WINDOW
            // and the DE flags BorgMate as "preventing logout".
            _sigTermRegistration = PosixSignalRegistration.Create(PosixSignal.SIGTERM, HandlePosixShutdown);
            _sigHupRegistration = PosixSignalRegistration.Create(PosixSignal.SIGHUP, HandlePosixShutdown);
            _sigIntRegistration = PosixSignalRegistration.Create(PosixSignal.SIGINT, HandlePosixShutdown);

            // Load icon for macOS dock
            MacOsDockHelper.LoadIcon(AssetLoader.Open(
                new Uri("avares://BorgMate/Assets/borgmate-256.png")));

            SetupTrayIcon(desktop);

            // Single-instance: bring window to front when second instance signals
            if (Program.InstanceGuard is not null)
            {
                Program.InstanceGuard.ActivationRequested += () =>
                    Dispatcher.UIThread.Post(ShowMainWindow);

                // A --scheduled-wake launch deferred to us: check schedules right away
                // instead of waiting up to a minute — the machine may re-sleep sooner.
                // The AC-only battery gate lives inside the scheduler's check itself.
                var scheduler = Services.GetRequiredService<IBackupScheduler>();
                Program.InstanceGuard.WakeRequested += () =>
                    Dispatcher.UIThread.Post(scheduler.RequestImmediateCheck);
            }

#if DEBUG
            if (Program.DemoMode)
                LoadDemoData();
#endif

            var startMinimized = _settings.StartMinimized;
#if DEBUG
            startMinimized = false;
#endif
            // A scheduled-wake launch always stays in the tray, in any configuration.
            if (Program.ScheduledWakeMode)
                startMinimized = true;
            if (!startMinimized)
                ShowMainWindow();

            if (_settings.CheckForUpdates && !Program.HideUpdateNotification)
                _ = CheckForUpdatesAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private async Task CheckForUpdatesAsync()
    {
        await _updateService.CheckForUpdatesAsync();

        if (!_updateService.IsUpdateAvailable || _updateService.AvailableVersion is null)
            return;

        var version = _updateService.AvailableVersion;
        var changelog = await _updateService.FetchChangelogAsync(BuildInfo.CurrentVersion, version);
        _status.SetUpdateAvailable(
            string.Format(Strings.Get("UpdateAvailable"), version),
            async () =>
            {
                if (!await ShowUpdateChangelogAsync(version, changelog))
                    return;

                _status.IsDownloading = true;
                _status.UpdateProgress = 0;
                _status.UpdateMessage = Strings.Get("DownloadingUpdate");
                var downloaded = await _updateService.DownloadUpdateAsync(pct =>
                    Dispatcher.UIThread.Post(() =>
                    {
                        _status.UpdateProgress = pct;
                        _status.UpdateMessage = $"{Strings.Get("DownloadingUpdate")} {pct}%";
                    }));
                _status.IsDownloading = false;

                if (!downloaded)
                {
                    _status.UpdateMessage = null;
                    return;
                }

                _status.UpdateMessage = null;
                if (await DialogHelper.ConfirmAsync(Strings.Get("RestartToUpdate")))
                    await _updateService.ApplyAndRestartAsync();
            });
    }

    private static async Task<bool> ShowUpdateChangelogAsync(
        string version, List<ChangelogEntry> changelog)
    {
        var window = DialogHelper.GetMainWindow();
        if (window is null) return false;

        var dialog = new UpdateChangelogWindow(version, changelog);
        await dialog.ShowDialog(window);
        return dialog.Confirmed;
    }

    private void EnsureMainWindow()
    {
        if (_mainWindow is not null) return;

        _mainWindow = new MainWindow { DataContext = _mainVm, Title = $"BorgMate v{BuildInfo.DisplayVersion}" };

        RestoreWindowState();

        _mainWindow.Closing += async (_, e) =>
        {
            // Let the close proceed when the OS session is shutting down, when the
            // Avalonia app lifetime is shutting down, or when our SIGTERM handler
            // has initiated quit. Otherwise treat close as "hide to tray".
            if (_isShuttingDown
                || e.CloseReason == WindowCloseReason.OSShutdown
                || e.CloseReason == WindowCloseReason.ApplicationShutdown)
                return;

            e.Cancel = true;
            CaptureWindowState();
            await SaveConfigSafelyAsync("window close");
            HideMainWindow();
        };

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = _mainWindow;
    }

    private void ShowMainWindow()
    {
        var firstShow = _mainWindow is null;
        EnsureMainWindow();
        MacOsDockHelper.ShowInDock();
        _mainWindow!.Show();

        if (!firstShow)
            _mainWindow.WindowState = WindowState.Normal;

        _mainWindow.Activate();
    }

    private void CaptureWindowState()
    {
        if (_mainWindow is null) return;

        _settings.IsMaximized = _mainWindow.WindowState == WindowState.Maximized;

        if (_mainWindow.WindowState != WindowState.Maximized)
        {
            _settings.WindowX = _mainWindow.Position.X;
            _settings.WindowY = _mainWindow.Position.Y;
            _settings.WindowWidth = _mainWindow.Width;
            _settings.WindowHeight = _mainWindow.Height;
        }
    }

    private void RestoreWindowState()
    {
        if (_mainWindow is null) return;

        if (_settings.WindowWidth.HasValue && _settings.WindowHeight.HasValue)
        {
            _mainWindow.Width = _settings.WindowWidth.Value;
            _mainWindow.Height = _settings.WindowHeight.Value;
        }

        if (_settings.WindowX.HasValue && _settings.WindowY.HasValue)
        {
            _mainWindow.Position = new PixelPoint((int)_settings.WindowX.Value, (int)_settings.WindowY.Value);
            _mainWindow.WindowStartupLocation = WindowStartupLocation.Manual;
        }

        if (_settings.IsMaximized)
            _mainWindow.WindowState = WindowState.Maximized;
    }

    private async void OnAboutClicked(object? sender, EventArgs e)
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            && desktop.MainWindow is not null)
        {
            var about = new AboutWindow();
            await about.ShowDialog(desktop.MainWindow);
        }
    }

    private void HideMainWindow()
    {
        _mainWindow?.Hide();
        MacOsDockHelper.HideFromDock();

        _cache.ClearAll();
        GC.Collect();
    }

    private async Task TryQuitAsync(IClassicDesktopStyleApplicationLifetime desktop, bool forceQuit = false)
    {
        if (!await CancelRunningJobsForShutdownAsync(forceQuit))
            return;

        await PrepareShutdownAsync();
        desktop.TryShutdown();
    }

    private async Task<bool> CancelRunningJobsForShutdownAsync(bool forceQuit)
    {
        if (!_jobQueue.HasRunningJobs)
            return true;

        if (!forceQuit && _jobQueue.HasPendingCommand)
        {
            ShowMainWindow();
            if (!await DialogHelper.ConfirmAsync(Strings.Get("ConfirmQuitWithRunningTasks")))
                return false;
        }

        // Mark shutdown early so any concurrent Closing events (from SIGTERM
        // races or desktop.Shutdown closing the window) don't re-hide to tray.
        _isShuttingDown = true;

        // Cancel running jobs and wait for their completion handlers
        // (journal writes). Cap the wait so a hung borg process — e.g.
        // stuck on SSH I/O — can't stall system shutdown indefinitely.
        var runningJobs = _jobQueue.Jobs
            .Where(j => j.Status is BorgJobStatus.Pending or BorgJobStatus.Running)
            .ToList();
        foreach (var job in runningJobs)
            job.Cts.Cancel();
        var waitAll = Task.WhenAll(runningJobs.Select(j => j.Completion.Task));
        await Task.WhenAny(waitAll, Task.Delay(TimeSpan.FromSeconds(5)));
        return true;
    }

    private Task PrepareShutdownAsync()
    {
        if (_shutdownPrepared)
            return Task.CompletedTask;

        _shutdownPreparationTask ??= PrepareShutdownCoreAsync();
        return _shutdownPreparationTask;
    }

    private async Task PrepareShutdownCoreAsync()
    {
        _logger.LogInformation("BorgMate v{Version} shutting down", BuildInfo.DisplayVersion);
        _isShuttingDown = true;
        CaptureWindowState();
        await SaveConfigSafelyAsync("shutdown");
        _jobQueue.Dispose();
        _sleepInhibitor.Release();
        _sigTermRegistration?.Dispose();
        _sigHupRegistration?.Dispose();
        _sigIntRegistration?.Dispose();
        _shutdownPrepared = true;
        (Services as IDisposable)?.Dispose();
    }

    private async Task SaveConfigSafelyAsync(string operation)
    {
        if (_mainVm is null)
            return;

        try
        {
            await _mainVm.SaveConfigAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save config during {Operation}", operation);
        }
    }

    /// <summary>
    ///     Handles POSIX shutdown signals (SIGTERM/SIGHUP/SIGINT). Cancels the
    ///     default termination, marks shutdown state synchronously (so any racing
    ///     Closing events let the window close), and dispatches to the UI thread
    ///     to run the normal quit path with forceQuit=true (skips confirm dialog).
    /// </summary>
    private void HandlePosixShutdown(PosixSignalContext context)
    {
        if (_isShuttingDown) return;
        context.Cancel = true;
        _isShuttingDown = true;
        _logger.LogInformation("Received {Signal}, initiating graceful shutdown", context.Signal);

        Dispatcher.UIThread.Post(async () =>
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                await TryQuitAsync(desktop, true);
        });
    }

    /// <summary>
    ///     Updates tray icon tooltip, dock progress bar, taskbar, and sleep inhibit state
    ///     based on running job state. Called from MainWindowViewModel's timer on the UI thread.
    ///     Sleep inhibit keys off HasPendingCommand so quick query jobs (archive lists, stats)
    ///     don't keep the machine awake — only backup/restore/prune/check/compact do.
    /// </summary>
    public static void UpdateRunningIndicator(bool hasRunningJobs, int count, double? progress)
    {
        if (_instance is not { } app) return;

        // Toggle sleep inhibit based on command-job state. Idempotent via _sleepInhibited flag.
        var hasCommand = app._jobQueue.HasPendingCommand;
        if (hasCommand && !app._sleepInhibited)
        {
            app._sleepInhibitor.Inhibit("BorgMate running a borg command");
            app._sleepInhibited = true;
        }
        else if (!hasCommand && app._sleepInhibited)
        {
            app._sleepInhibitor.Release();
            app._sleepInhibited = false;
        }

        // Skip redundant "idle" updates
        if (!hasRunningJobs && !app._hadRunningJobs) return;
        app._hadRunningJobs = hasRunningJobs;

        if (app._trayIcon is not null)
            app._trayIcon.ToolTipText = hasRunningJobs
                ? $"BorgMate — {string.Format(Strings.Get("TrayJobsRunning"), count)}"
                : "BorgMate";

        if (OperatingSystem.IsMacOS())
            MacOsDockHelper.SetProgress(hasRunningJobs ? progress ?? -1 : null);

        if (OperatingSystem.IsWindows())
            UpdateWindowsTaskbar(app, hasRunningJobs);
    }

    [SuppressMessage("Interoperability", "CA1416:Validate platform compatibility")]
    private static void UpdateWindowsTaskbar(App app, bool hasRunningJobs)
    {
        var hwnd = app._mainWindow?.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (hasRunningJobs)
            WindowsTaskbarHelper.SetIndeterminate(hwnd);
        else
            WindowsTaskbarHelper.Clear(hwnd);
    }

    private void SetupTrayIcon(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var openItem = new NativeMenuItem(Strings.Get("OpenBorgMate"));
        openItem.Click += (_, _) => ShowMainWindow();

        var quitItem = new NativeMenuItem(Strings.Get("Quit"));
        quitItem.Click += async (_, _) => await TryQuitAsync(desktop);

        var menu = new NativeMenu();
        menu.Items.Add(openItem);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(quitItem);

        _trayIcon = new TrayIcon
        {
            ToolTipText = "BorgMate",
            Menu = menu,
            Icon = new WindowIcon(AssetLoader.Open(
                new Uri("avares://BorgMate/Assets/borgmate.ico")))
        };

        _trayIcon.Clicked += (_, _) =>
        {
            if (_mainWindow?.IsVisible == true)
                HideMainWindow();
            else
                ShowMainWindow();
        };

        _trayIcon.IsVisible = true;
    }

#if DEBUG
    private void LoadDemoData()
    {
        if (_mainVm is not null)
            DemoDataLoader.Load(_mainVm,
                Services.GetRequiredService<IJournalStore>(),
                _status, ShowUpdateChangelogAsync, !Program.HideUpdateNotification);
    }
#endif

    private static ServiceProvider ConfigureServices()
    {
        return new ServiceCollection().ConfigureBorgMateServices();
    }
}
