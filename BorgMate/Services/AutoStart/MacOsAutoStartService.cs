using System;
using System.IO;
using System.Reflection;

namespace BorgMate.Services.AutoStart;

internal class MacOsAutoStartService : IAutoStartService
{
    private static string LaunchAgentPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library", "LaunchAgents", "com.borgmate.app.plist");

    public void SetEnabled(bool enabled)
    {
        if (enabled)
            WritePlist();
        else if (File.Exists(LaunchAgentPath))
            File.Delete(LaunchAgentPath);
    }

    public bool IsEnabled() => File.Exists(LaunchAgentPath);

    private static void WritePlist()
    {
        var appPath = GetAppPath();
        var isBundle = appPath.EndsWith(".app", StringComparison.OrdinalIgnoreCase);

        var templateName = isBundle
            ? "BorgMate.Resources.Platform.launchagent-bundle.plist"
            : "BorgMate.Resources.Platform.launchagent-binary.plist";

        var template = ReadEmbeddedResource(templateName);
        var plist = template.Replace("{{APP_PATH}}", EscapeXml(appPath));

        var dir = Path.GetDirectoryName(LaunchAgentPath)!;
        Directory.CreateDirectory(dir);
        File.WriteAllText(LaunchAgentPath, plist);
    }

    private static string ReadEmbeddedResource(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Embedded resource '{name}' not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string GetAppPath()
    {
        var mainModule = Environment.ProcessPath;
        if (mainModule is not null)
        {
            // Walk up from Contents/MacOS/BorgMate to find .app bundle root
            var dir = Path.GetDirectoryName(mainModule);
            while (dir is not null)
            {
                if (dir.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
                    return dir;
                dir = Path.GetDirectoryName(dir);
            }
        }
        return mainModule ?? "BorgMate";
    }

    private static string EscapeXml(string value) =>
        value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
