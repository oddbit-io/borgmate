using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using BorgMate.Localization;
using BorgMate.Models;
using BorgMate.Services.Journal;
using BorgMate.Services.Power;
using BorgMate.Services.Queue;
using BorgMate.ViewModels;
using BorgMate.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BorgMate;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;
    public static string AppVersion { get; private set; } = "dev";
    private static App? _instance;
    private TrayIcon? _trayIcon;
    private MainWindow? _mainWindow;
    private MainWindowViewModel? _mainVm;
    private AppSettings _settings = null!;
    private JobQueueService _jobQueue = null!;
    private BorgCacheService _cache = null!;
    private StatusService _status = null!;
    private UpdateService _updateService = null!;
    private ISleepInhibitor _sleepInhibitor = null!;
    private ILogger<App> _logger = null!;
    private bool _hadRunningJobs;
    private bool _sleepInhibited;

    public override void Initialize()
    {
        if (!Design.IsDesignMode)
            Strings.ApplyLanguageCode(new ConfigService().Load().Settings.Language);

        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        _instance = this;
        Services = ConfigureServices();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            AppVersion = version is not null ? $"{version.Major}.{version.Minor}.{version.Build}" : "dev";

            _settings = Services.GetRequiredService<AppSettings>();
            _jobQueue = Services.GetRequiredService<JobQueueService>();
            _cache = Services.GetRequiredService<BorgCacheService>();
            _status = Services.GetRequiredService<StatusService>();
            _updateService = Services.GetRequiredService<UpdateService>();
            _sleepInhibitor = Services.GetRequiredService<ISleepInhibitor>();
            _mainVm = Services.GetRequiredService<MainWindowViewModel>();
            _logger = Services.GetRequiredService<ILogger<App>>();

            _logger.LogInformation("BorgMate v{Version} starting on {OS} {Arch}",
                AppVersion, System.Runtime.InteropServices.RuntimeInformation.OSDescription,
                System.Runtime.InteropServices.RuntimeInformation.OSArchitecture);
            var journal = Services.GetRequiredService<IJournalService>();
            journal.Load();

            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            desktop.ShutdownRequested += (_, _) =>
            {
                CaptureWindowState();
                _mainVm.SaveConfig();
                _jobQueue.Dispose();
                _sleepInhibitor.Release();
                _logger.LogInformation("BorgMate v{Version} shutting down", AppVersion);
            };

            // Load icon for macOS dock
            MacOsDockHelper.LoadIcon(Avalonia.Platform.AssetLoader.Open(
                new Uri("avares://BorgMate/Assets/borgmate-256.png")));

            SetupTrayIcon(desktop);

            // Single-instance: bring window to front when second instance signals
            if (Program.InstanceGuard is not null)
                Program.InstanceGuard.ActivationRequested += () =>
                    Avalonia.Threading.Dispatcher.UIThread.Post(ShowMainWindow);

#if DEBUG
            if (Program.DemoMode)
                LoadDemoData();
#endif

            if (!_settings.StartMinimized)
                ShowMainWindow();

            if (_settings.CheckForUpdates)
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
        var changelog = await _updateService.FetchChangelogAsync(AppVersion, version);
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
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
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
                    _updateService.ApplyAndRestart();
            });
    }

    private static async Task<bool> ShowUpdateChangelogAsync(
        string version, List<UpdateService.ChangelogEntry> changelog)
    {
        var window = DialogHelper.GetMainWindow();
        if (window is null) return false;

        var dialog = new Views.UpdateChangelogWindow(version, changelog);
        await dialog.ShowDialog(window);
        return dialog.Confirmed;
    }

    private void EnsureMainWindow()
    {
        if (_mainWindow is not null) return;

        _mainWindow = new MainWindow { DataContext = _mainVm, Title = $"BorgMate v{AppVersion}" };

        RestoreWindowState();

        _mainWindow.Closing += (_, e) =>
        {
            e.Cancel = true;
            CaptureWindowState();
            _mainVm?.SaveConfig();
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

    private async void OnAboutClicked(object? sender, System.EventArgs e)
    {
        if (ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            && desktop.MainWindow is not null)
        {
            var about = new Views.AboutWindow();
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

    private async Task TryQuitAsync(IClassicDesktopStyleApplicationLifetime desktop)
    {
        if (_jobQueue.HasRunningJobs)
        {
            if (_jobQueue.HasPendingCommand)
            {
                ShowMainWindow();
                if (!await DialogHelper.ConfirmAsync(Strings.Get("ConfirmQuitWithRunningTasks")))
                    return;
            }

            // Cancel running jobs and wait for completion handlers (journal writes)
            var runningJobs = _jobQueue.Jobs
                .Where(j => j.Status is BorgJobStatus.Pending or BorgJobStatus.Running)
                .ToList();
            foreach (var job in runningJobs)
                job.Cts.Cancel();
            await Task.WhenAll(runningJobs.Select(j => j.Completion.Task));
        }

        desktop.Shutdown();
    }

    /// <summary>
    /// Updates tray icon tooltip, dock progress bar, taskbar, and sleep inhibit state
    /// based on running job state. Called from MainWindowViewModel's timer on the UI thread.
    /// Sleep inhibit keys off HasPendingCommand so quick query jobs (archive lists, stats)
    /// don't keep the machine awake — only backup/restore/prune/check/compact do.
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

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Interoperability", "CA1416:Validate platform compatibility")]
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
            Icon = new WindowIcon(Avalonia.Platform.AssetLoader.Open(
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
            BorgMate.Services.Mocks.DemoDataLoader.Load(_mainVm,
                Services.GetRequiredService<IJournalService>(),
                _status, ShowUpdateChangelogAsync);
    }
#endif

    private static ServiceProvider ConfigureServices() =>
        new ServiceCollection().ConfigureBorgMateServices();

}
