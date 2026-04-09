using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace BorgMate.Services;

/// <summary>
/// Handles WSL path conversion and command wrapping for running borg on Windows.
/// Always active on Windows — borg runs inside WSL.
/// </summary>
public partial class WslHelper(ILogger<WslHelper> logger)
{
    private bool? _wslAvailable;
    private string? _wslHome;

    public static bool IsRequired => OperatingSystem.IsWindows();

    public bool IsAvailable
    {
        get
        {
            if (!OperatingSystem.IsWindows()) return false;
            _wslAvailable ??= CheckWslAvailable();
            return _wslAvailable.Value;
        }
    }

    private bool CheckWslAvailable()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "wsl",
                Arguments = "--list --quiet",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi);
            if (process is null) return false;
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "WSL availability check failed");
            return false;
        }
    }

    /// <summary>
    /// Converts a Windows path to a WSL path.
    /// C:\Users\foo → /mnt/c/Users/foo
    /// Paths that are already Unix-style or SSH paths are returned as-is.
    /// </summary>
    public static string ToWslPath(string windowsPath)
    {
        if (string.IsNullOrWhiteSpace(windowsPath))
            return windowsPath;

        if (windowsPath.StartsWith('/') || windowsPath.Contains('@'))
            return windowsPath;

        var match = DriveLetterRegex().Match(windowsPath);
        if (match.Success)
        {
            var drive = match.Groups[1].Value.ToLowerInvariant();
            var rest = match.Groups[2].Value.Replace('\\', '/');
            return $"/mnt/{drive}/{rest}";
        }

        return windowsPath.Replace('\\', '/');
    }

    /// <summary>
    /// Wraps a borg command to run inside WSL via bash -c.
    /// Environment variables are exported, CWD is set to ~ by default
    /// to avoid Windows CWD issues.
    /// </summary>
    public static (string fileName, string arguments) WrapCommand(
        string borgBinary, string arguments,
        Dictionary<string, string>? environment,
        string? workingDirectory = null,
        List<string>? preCommands = null)
    {
        var cdTarget = workingDirectory is not null ? $"cd {ShellEscape(workingDirectory)}" : "cd ~";
        var parts = new List<string> { cdTarget };

        if (environment is { Count: > 0 })
        {
            foreach (var (key, value) in environment)
                parts.Add($"export {key}={ShellEscape(value)}");
        }

        if (preCommands is { Count: > 0 })
            parts.AddRange(preCommands);

        parts.Add($"{borgBinary} {arguments}");

        var cmd = string.Join(" && ", parts);
        var wslArgs = $"-- bash -c {ShellEscape(cmd)}";
        return ("wsl", wslArgs);
    }

    /// <summary>
    /// ANSI-C quoting: wraps value in $'...' with proper escaping.
    /// </summary>
    public static string ShellEscapePublic(string value) => ShellEscape(value);
    internal static string ShellEscape(string value) =>
        "$'" + value.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\n", "\\n") + "'";

    /// <summary>
    /// Copies a Windows SSH key into WSL's ~/.ssh/ with correct permissions (chmod 600).
    /// Call this when saving repo config so the key is ready for borg operations.
    /// </summary>
    public async Task<string?> CopySshKeyAsync(string windowsKeyPath)
    {
        if (!IsAvailable || string.IsNullOrWhiteSpace(windowsKeyPath))
            return null;

        var home = await GetWslHomeAsync();
        if (home is null) return null;

        var keyName = Path.GetFileName(windowsKeyPath);
        var wslSrc = ToWslPath(windowsKeyPath);
        var wslDest = $"{home}/.ssh/{keyName}";

        var psi = new ProcessStartInfo
        {
            FileName = "wsl",
            Arguments = $"-- bash -c {ShellEscape($"mkdir -p ~/.ssh && cp \"{wslSrc}\" \"{wslDest}\" && chmod 600 \"{wslDest}\"")}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            using var process = Process.Start(psi);
            if (process is null) return null;
            await process.WaitForExitAsync();
            return process.ExitCode == 0 ? wslDest : null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to copy SSH key to WSL");
            return null;
        }
    }

    /// <summary>
    /// Returns the WSL-native key path for use in BORG_RSH.
    /// </summary>
    public async Task<string?> GetWslKeyPathAsync(string windowsKeyPath)
    {
        if (string.IsNullOrWhiteSpace(windowsKeyPath)) return null;
        var home = await GetWslHomeAsync();
        if (home is null) return null;
        return $"{home}/.ssh/{Path.GetFileName(windowsKeyPath)}";
    }

    private async Task<string?> GetWslHomeAsync()
    {
        if (_wslHome is not null) return _wslHome;

        var psi = new ProcessStartInfo
        {
            FileName = "wsl",
            Arguments = "-- bash -c \"echo $HOME\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            using var process = Process.Start(psi);
            if (process is null) return null;
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
                _wslHome = output.Trim();
            return _wslHome;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to get WSL home directory");
            return null;
        }
    }

    /// <summary>
    /// Kills borg processes running inside WSL. Called on cancellation
    /// because Process.Kill on wsl.exe doesn't reach the Linux namespace.
    /// </summary>
    public static void KillBorgProcesses(string borgBinary)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "wsl",
            Arguments = $"-- pkill -f {ShellEscape(borgBinary)}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var process = Process.Start(psi);
        process?.WaitForExit(3000);
    }

    [GeneratedRegex(@"^([A-Za-z]):[/\\](.*)$")]
    private static partial Regex DriveLetterRegex();
}
