using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace BorgMate.Infrastructure.Platform;

/// <summary>
///     Handles WSL path conversion and command wrapping for running borg on Windows.
///     Always active on Windows — borg runs inside WSL.
/// </summary>
public partial class WslHelper
{
    private static readonly TimeSpan WslCommandTimeout = TimeSpan.FromSeconds(5);

    private readonly IWslAvailabilityProbe _availabilityProbe;
    private readonly SemaphoreSlim _availabilityGate = new(1, 1);
    private readonly bool _isRequired;
    private readonly ILogger<WslHelper> _logger;
    private bool? _wslAvailable;
    private string? _wslHome;

    public WslHelper(ILogger<WslHelper> logger)
        : this(logger, new WslAvailabilityProbe(logger), IsRequired)
    {
    }

    internal WslHelper(ILogger<WslHelper> logger, IWslAvailabilityProbe availabilityProbe, bool isRequired)
    {
        _logger = logger;
        _availabilityProbe = availabilityProbe;
        _isRequired = isRequired;
    }

    public static bool IsRequired => OperatingSystem.IsWindows();

    /// <summary>Returns the last cached WSL availability result without starting a process.</summary>
    public bool IsAvailable => _isRequired && _wslAvailable == true;

    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        if (!_isRequired)
            return false;

        if (_wslAvailable is { } cached)
            return cached;

        await _availabilityGate.WaitAsync(ct);
        try
        {
            if (_wslAvailable is { } cachedAfterWait)
                return cachedAfterWait;

            _wslAvailable = await _availabilityProbe.IsAvailableAsync(ct);
            return _wslAvailable.Value;
        }
        finally
        {
            _availabilityGate.Release();
        }
    }

    /// <summary>
    ///     Converts a Windows path to a WSL path.
    ///     C:\Users\foo → /mnt/c/Users/foo
    ///     Paths that are already Unix-style or SSH paths are returned as-is.
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
    ///     Wraps a borg command to run inside WSL via bash -c.
    ///     Environment variables are exported, CWD is set to ~ by default
    ///     to avoid Windows CWD issues.
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
            foreach (var (key, value) in environment)
                parts.Add($"export {key}={ShellEscape(value)}");

        if (preCommands is { Count: > 0 })
            parts.AddRange(preCommands);

        parts.Add($"{borgBinary} {arguments}");

        var cmd = string.Join(" && ", parts);
        var wslArgs = $"-- bash -c {ShellEscape(cmd)}";
        return ("wsl", wslArgs);
    }

    /// <summary>
    ///     ANSI-C quoting: wraps value in $'...' with proper escaping.
    /// </summary>
    public static string ShellEscapePublic(string value)
    {
        return ShellEscape(value);
    }

    internal static string ShellEscape(string value)
    {
        return "$'" + value.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\n", "\\n") + "'";
    }

    /// <summary>
    ///     Copies a Windows SSH key into WSL's ~/.ssh/ with correct permissions (chmod 600).
    ///     Call this when saving repo config so the key is ready for borg operations.
    /// </summary>
    public async Task<string?> CopySshKeyAsync(string windowsKeyPath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(windowsKeyPath) || !await IsAvailableAsync(ct))
            return null;

        var home = await GetWslHomeAsync(ct);
        if (home is null) return null;

        var keyName = Path.GetFileName(windowsKeyPath);
        var wslSrc = ToWslPath(windowsKeyPath);
        var wslDest = $"{home}/.ssh/{keyName}";

        var psi = new ProcessStartInfo
        {
            FileName = "wsl",
            Arguments =
                $"-- bash -c {ShellEscape($"mkdir -p ~/.ssh && cp \"{wslSrc}\" \"{wslDest}\" && chmod 600 \"{wslDest}\"")}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            using var process = Process.Start(psi);
            if (process is null) return null;
            if (!await WaitForWslCommandAsync(process, "copy SSH key", ct))
                return null;
            return process.ExitCode == 0 ? wslDest : null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to copy SSH key to WSL");
            return null;
        }
    }

    /// <summary>
    ///     Returns the WSL-native key path for use in BORG_RSH.
    /// </summary>
    public async Task<string?> GetWslKeyPathAsync(string windowsKeyPath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(windowsKeyPath)) return null;
        if (!await IsAvailableAsync(ct)) return null;
        var home = await GetWslHomeAsync(ct);
        if (home is null) return null;
        return $"{home}/.ssh/{Path.GetFileName(windowsKeyPath)}";
    }

    private async Task<string?> GetWslHomeAsync(CancellationToken ct = default)
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
            if (!await WaitForWslCommandAsync(process, "get WSL home directory", ct))
                return null;
            var output = await process.StandardOutput.ReadToEndAsync(ct);
            if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
                _wslHome = output.Trim();
            return _wslHome;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get WSL home directory");
            return null;
        }
    }

    private async Task<bool> WaitForWslCommandAsync(Process process, string operationName, CancellationToken ct)
    {
        using var timeoutCts = new CancellationTokenSource(WslCommandTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        try
        {
            await process.WaitForExitAsync(linkedCts.Token);
            return true;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested && timeoutCts.IsCancellationRequested)
        {
            TryKill(process);
            _logger.LogWarning("Timed out while trying to {OperationName} in WSL", operationName);
            return false;
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(true);
        }
        catch
        {
            /* already exited */
        }
    }

    /// <summary>
    ///     Kills borg processes running inside WSL. Called on cancellation
    ///     because Process.Kill on wsl.exe doesn't reach the Linux namespace.
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
