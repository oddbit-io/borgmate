using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;
using System.Text;
using Avalonia.Platform;
using BorgMate.Infrastructure.Platform;
using Microsoft.Win32;

namespace BorgMate.Infrastructure.Notifications;

[SupportedOSPlatform("windows")]
public class WindowsNotificationService : INotificationService
{
    internal const string ToastXmlVariable = "BORGMATE_TOAST_XML";
    internal const string ToastAppIdVariable = "BORGMATE_TOAST_APP_ID";

    // Notification data reaches PowerShell only through environment variables, so the script text stays constant.
    internal const string ToastScript =
        "[Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime] | Out-Null\n" +
        "[Windows.Data.Xml.Dom.XmlDocument, Windows.Data.Xml.Dom.XmlDocument, ContentType = WindowsRuntime] | Out-Null\n" +
        "$xd = [Windows.Data.Xml.Dom.XmlDocument]::new()\n" +
        $"$xd.LoadXml($env:{ToastXmlVariable})\n" +
        "$t = [Windows.UI.Notifications.ToastNotification]::new($xd)\n" +
        $"[Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier($env:{ToastAppIdVariable}).Show($t)";

    private static bool _registered;
    private static string? _iconPath;

    public void Send(string title, string body)
    {
        EnsureRegistered();
        Process.Start(BuildToastStartInfo(StringHelpers.AppName, _iconPath, title, body))?.Dispose();
    }

    internal static ProcessStartInfo BuildToastStartInfo(string appUserModelId, string? iconPath, string title,
        string body)
    {
        var iconAttr = iconPath is not null
            ? $" hint-crop=\"circle\" src=\"file:///{StringHelpers.EscapeXml(iconPath.Replace('\\', '/'))}\""
            : "";
        var xml = "<toast><visual><binding template=\"ToastGeneric\">" +
                  $"<image placement=\"appLogoOverride\"{iconAttr}/>" +
                  $"<text>{StringHelpers.AppName} \u2014 {StringHelpers.EscapeXml(title)}</text>" +
                  $"<text>{StringHelpers.EscapeXml(body)}</text>" +
                  "</binding></visual></toast>";

        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(ToastScript));
        var startInfo = new ProcessStartInfo("powershell", $"-NoProfile -NonInteractive -EncodedCommand {encoded}")
        {
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.Environment[ToastXmlVariable] = xml;
        startInfo.Environment[ToastAppIdVariable] = appUserModelId;
        return startInfo;
    }

    private static void EnsureRegistered()
    {
        if (_registered) return;
        _registered = true;

        try
        {
            // Extract icon to AppData for toast notification use
            _iconPath = ExtractIcon();

            using var key =
                Registry.CurrentUser.CreateSubKey($@"Software\Classes\AppUserModelId\{StringHelpers.AppName}");
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
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                StringHelpers.AppName);
            var iconPath = Path.Combine(dir, "notification-icon.png");
            if (File.Exists(iconPath)) return iconPath;

            Directory.CreateDirectory(dir);
            var uri = new Uri($"avares://{StringHelpers.AppName}/Assets/borgmate-256.png");
            using var stream = AssetLoader.Open(uri);
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
