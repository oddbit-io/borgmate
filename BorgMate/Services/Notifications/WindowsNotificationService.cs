using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32;

namespace BorgMate.Services.Notifications;

[SupportedOSPlatform("windows")]
public class WindowsNotificationService : INotificationService
{
    private static bool _registered;
    private static string? _iconPath;

    public void Send(string title, string body)
    {
        EnsureRegistered();

        var iconAttr = _iconPath is not null
            ? $" hint-crop=\"circle\" src=\"file:///{_iconPath.Replace('\\', '/')}\""
            : "";
        var xml = "<toast><visual><binding template=\"ToastGeneric\">" +
                  $"<image placement=\"appLogoOverride\"{iconAttr}/>" +
                  $"<text>{StringHelpers.AppName} \u2014 {StringHelpers.EscapeXml(title)}</text>" +
                  $"<text>{StringHelpers.EscapeXml(body)}</text>" +
                  "</binding></visual></toast>";

        var psXml = xml.Replace("'", "''");
        var ps = "[Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime] | Out-Null\n" +
                 "[Windows.Data.Xml.Dom.XmlDocument, Windows.Data.Xml.Dom.XmlDocument, ContentType = WindowsRuntime] | Out-Null\n" +
                 "$xd = [Windows.Data.Xml.Dom.XmlDocument]::new()\n" +
                 $"$xd.LoadXml('{psXml}')\n" +
                 "$t = [Windows.UI.Notifications.ToastNotification]::new($xd)\n" +
                 $"[Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('{StringHelpers.AppName}').Show($t)";

        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(ps));

        Process.Start(new ProcessStartInfo("powershell", $"-NoProfile -EncodedCommand {encoded}")
        {
            UseShellExecute = false,
            CreateNoWindow = true
        })?.Dispose();
    }

    private static void EnsureRegistered()
    {
        if (_registered) return;
        _registered = true;

        try
        {
            // Extract icon to AppData for toast notification use
            _iconPath = ExtractIcon();

            using var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\AppUserModelId\{StringHelpers.AppName}");
            key?.SetValue("DisplayName", StringHelpers.AppName);
            if (_iconPath is not null)
                key?.SetValue("IconUri", _iconPath);
        }
        catch
        {
            // Non-critical
        }
    }

    private static string? ExtractIcon()
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), StringHelpers.AppName);
            var iconPath = Path.Combine(dir, "notification-icon.png");
            if (File.Exists(iconPath)) return iconPath;

            Directory.CreateDirectory(dir);
            var uri = new Uri($"avares://{StringHelpers.AppName}/Assets/borgmate-256.png");
            using var stream = Avalonia.Platform.AssetLoader.Open(uri);
            using var fs = File.Create(iconPath);
            stream.CopyTo(fs);
            return iconPath;
        }
        catch
        {
            return null;
        }
    }

}
