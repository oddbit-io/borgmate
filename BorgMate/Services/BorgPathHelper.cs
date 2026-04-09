using System;
using System.Diagnostics;
using System.IO;

namespace BorgMate.Services;

public static class BorgPathHelper
{
    private static readonly string[] MacOsPaths =
    [
        "/opt/homebrew/bin/borg",
        "/usr/local/bin/borg",
        "/opt/local/bin/borg",
    ];

    private static readonly string[] LinuxPaths =
    [
        "/usr/bin/borg",
        "/usr/local/bin/borg",
        "/snap/bin/borg",
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local/bin/borg"),
        "/home/linuxbrew/.linuxbrew/bin/borg",
    ];

    public static string Detect()
    {
        var candidates = OperatingSystem.IsMacOS() ? MacOsPaths
            : OperatingSystem.IsLinux() ? LinuxPaths
            : Array.Empty<string>();

        foreach (var path in candidates)
        {
            if (File.Exists(path))
                return path;
        }

        // Fallback: ask the shell (catches Nix, custom installs, etc.)
        if (!OperatingSystem.IsWindows())
        {
            var found = RunWhich("borg");
            if (found is not null)
                return found;
        }

        // Windows: borg runs inside WSL, resolved via PATH in bash -lc
        return "borg";
    }

    private static string? RunWhich(string binary)
    {
        try
        {
            var psi = new ProcessStartInfo("which", binary)
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var proc = Process.Start(psi);
            if (proc is null) return null;
            var output = proc.StandardOutput.ReadToEnd().Trim();
            proc.WaitForExit(3000);
            return proc.ExitCode == 0 && output.Length > 0 ? output : null;
        }
        catch
        {
            return null;
        }
    }
}
