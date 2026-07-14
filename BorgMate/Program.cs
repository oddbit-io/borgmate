using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using BorgMate.Infrastructure.Notifications;
using BorgMate.Infrastructure.Platform;
using Microsoft.Extensions.Logging.Abstractions;
using Velopack;

namespace BorgMate;

internal sealed class Program
{
    internal static SingleInstanceGuard? InstanceGuard;
    internal static bool ScheduledWakeMode;
    internal static bool HideUpdateNotification;
#if DEBUG
    internal static bool DemoMode;
#endif

    [STAThread]
    public static void Main(string[] args)
    {
        ScheduledWakeMode = ParseScheduledWakeMode(args);
#if DEBUG
        DemoMode = Array.Exists(args, a => a == "--demo");
        HideUpdateNotification = DemoMode && Array.Exists(args, a => a == "--hide-update");

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

        InstanceGuard = new SingleInstanceGuard();
        if (!InstanceGuard.TryAcquire(activateExisting: !ScheduledWakeMode))
            // Another instance is running and was signaled (activate or wake check)
            return;

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            InstanceGuard.Dispose();
        }
    }

    internal static bool ParseScheduledWakeMode(string[] args)
    {
        return Array.Exists(args, a => string.Equals(a, "--scheduled-wake", StringComparison.Ordinal));
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
        catch
        {
            /* last resort, don't throw */
        }
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
                svc = new LinuxNotificationService(NullLogger<LinuxNotificationService>.Instance);
            else
#pragma warning disable CA1416
                svc = new WindowsNotificationService();
#pragma warning restore CA1416

            svc.Send("Test Backup 'My Repo'", "Completed");
            Console.WriteLine("Notification sent. Waiting 3 seconds...");
            Thread.Sleep(3000);
            Console.WriteLine("Done.");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex}");
        }
    }
#endif

    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace()
            .With(new MacOSPlatformOptions { ShowInDock = false });
    }
}
