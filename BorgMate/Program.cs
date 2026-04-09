using Avalonia;
using System;
using System.IO;
using System.Threading.Tasks;
using Velopack;

namespace BorgMate;

sealed class Program
{
    internal static SingleInstanceGuard? InstanceGuard;
#if DEBUG
    internal static bool DemoMode;
#endif

    [STAThread]
    public static void Main(string[] args)
    {
#if DEBUG
        DemoMode = Array.Exists(args, a => a == "--demo");

        if (Array.Exists(args, a => a == "--test-notification"))
        {
            TestNotification();
            return;
        }
#endif

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            LogCrash($"[FATAL] Unhandled exception: {e.ExceptionObject}");

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            LogCrash($"[ERROR] Unobserved task exception: {e.Exception}");
            e.SetObserved();
        };

        VelopackApp.Build().Run();

        MigrateAppDataFolder();

        InstanceGuard = new SingleInstanceGuard();
        if (!InstanceGuard.TryAcquire())
        {
            // Another instance is running and was signaled to activate
            return;
        }

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            InstanceGuard.Dispose();
        }
    }

    /// <summary>
    /// Rename legacy BorgUI appdata folder to BorgMate for seamless config migration.
    /// </summary>
    private static void MigrateAppDataFolder()
    {
        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var oldDir = Path.Combine(appData, "BorgUI");
            var newDir = Path.Combine(appData, "BorgMate");
            if (Directory.Exists(oldDir) && !Directory.Exists(newDir))
                Directory.Move(oldDir, newDir);
        }
        catch (Exception ex) { Console.Error.WriteLine($"Config migration failed: {ex.Message}"); }
    }

    private static void LogCrash(string message)
    {
        Console.Error.WriteLine(message);
        try
        {
            var logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BorgMate", "logs");
            Directory.CreateDirectory(logDir);
            File.AppendAllText(
                Path.Combine(logDir, $"crash-{DateTime.Now:yyyy-MM-dd}.log"),
                $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        }
        catch { /* last resort, don't throw */ }
    }

#if DEBUG
    private static void TestNotification()
    {
        Console.WriteLine("Testing native notification...");
        try
        {
            INotificationService svc;
            if (OperatingSystem.IsMacOS())
                svc = new MacOsNotificationService();
            else if (OperatingSystem.IsLinux())
                svc = new LinuxNotificationService();
            else
#pragma warning disable CA1416
                svc = new WindowsNotificationService();
#pragma warning restore CA1416

            svc.Send("Test Backup 'My Repo'", "Completed");
            Console.WriteLine("Notification sent. Waiting 3 seconds...");
            System.Threading.Thread.Sleep(3000);
            Console.WriteLine("Done.");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex}");
        }
    }
#endif

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace()
            .With(new MacOSPlatformOptions { ShowInDock = false });
}
