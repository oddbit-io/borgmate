using System;
using System.IO;
using System.Reflection;
using System.Text;

namespace BorgMate.Infrastructure.Platform;

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

    public bool IsEnabled()
    {
        return File.Exists(AutoStartPath);
    }

    /// <summary>
    ///     AppImage: ProcessPath points to a FUSE temp mount; $APPIMAGE is the stable path.
    /// </summary>
    internal static string GetLauncherPath()
    {
        return Environment.GetEnvironmentVariable("APPIMAGE")
               ?? Environment.ProcessPath
               ?? throw new InvalidOperationException("The BorgMate executable path is unknown.");
    }

    /// <summary>
    ///     Quotes one argument of a Desktop Entry <c>Exec</c> key. A raw path with spaces would split
    ///     into several arguments, and <c>%</c> or a line break would add field codes or keys.
    /// </summary>
    internal static string QuoteExecArgument(string argument)
    {
        if (argument.IndexOfAny(['\n', '\r']) >= 0)
            throw new InvalidOperationException("A line break cannot appear in a desktop entry Exec value.");

        var quoted = new StringBuilder("\"");
        foreach (var c in argument)
        {
            if (c is '"' or '`' or '$' or '\\')
                quoted.Append('\\');
            quoted.Append(c);
        }

        quoted.Append('"');
        // Exec is also a string value, whose own escaping doubles every backslash.
        return quoted.ToString().Replace("\\", "\\\\").Replace("%", "%%");
    }

    private static void WriteDesktopEntry()
    {
        using var stream = Assembly.GetExecutingAssembly()
                               .GetManifestResourceStream("BorgMate.Resources.Platform.borgmate-autostart.desktop")
                           ?? throw new InvalidOperationException(
                               "Embedded resource 'borgmate-autostart.desktop' not found.");
        using var reader = new StreamReader(stream);
        var template = reader.ReadToEnd();

        var content = template.Replace("{{APP_PATH}}", QuoteExecArgument(GetLauncherPath()));

        var dir = Path.GetDirectoryName(AutoStartPath)!;
        Directory.CreateDirectory(dir);
        File.WriteAllText(AutoStartPath, content);
    }
}
