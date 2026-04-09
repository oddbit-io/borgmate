using System;
using System.IO;
using System.Reflection;

namespace BorgMate.Services.AutoStart;

internal class LinuxAutoStartService : IAutoStartService
{
    private static string AutoStartPath
    {
        get
        {
            var configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            if (string.IsNullOrEmpty(configHome))
                configHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
            return Path.Combine(configHome, "autostart", "borgmate.desktop");
        }
    }

    public void SetEnabled(bool enabled)
    {
        if (enabled)
            WriteDesktopEntry();
        else if (File.Exists(AutoStartPath))
            File.Delete(AutoStartPath);
    }

    public bool IsEnabled() => File.Exists(AutoStartPath);

    private static void WriteDesktopEntry()
    {
        // Use $APPIMAGE when running as an AppImage (ProcessPath points to a temp mount)
        var appPath = Environment.GetEnvironmentVariable("APPIMAGE")
                      ?? Environment.ProcessPath
                      ?? "BorgMate";

        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("BorgMate.Resources.Platform.borgmate-autostart.desktop")
            ?? throw new InvalidOperationException("Embedded resource 'borgmate-autostart.desktop' not found.");
        using var reader = new StreamReader(stream);
        var template = reader.ReadToEnd();

        var content = template.Replace("{{APP_PATH}}", appPath);

        var dir = Path.GetDirectoryName(AutoStartPath)!;
        Directory.CreateDirectory(dir);
        File.WriteAllText(AutoStartPath, content);
    }
}
