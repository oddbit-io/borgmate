using System.Diagnostics;
using System.IO;

namespace BorgMate.Services.Notifications;

public class LinuxNotificationService : INotificationService
{
    private readonly bool _hasNotifySend = File.Exists("/usr/bin/notify-send");

    public void Send(string title, string body)
    {
        if (!_hasNotifySend) return;

        Process.Start(new ProcessStartInfo("notify-send",
            $"\"{StringHelpers.EscapeShell(StringHelpers.AppName + " \u2014 " + title)}\" \"{StringHelpers.EscapeShell(body)}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true
        })?.Dispose();
    }
}
