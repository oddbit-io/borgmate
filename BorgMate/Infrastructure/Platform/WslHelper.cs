using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BorgMate.Infrastructure.Borg;
using Microsoft.Extensions.Logging;

namespace BorgMate.Infrastructure.Platform;

/// <summary>
///     Handles WSL path conversion and command wrapping for running borg on Windows.
///     Always active on Windows — borg runs inside WSL.
/// </summary>
public partial class WslHelper
{
    private const string WslEnvVariable = "WSLENV";

    /// <summary>Marks every Linux process of one WSL command, including the ssh children borg starts.</summary>
    public const string JobIdVariable = "BORGMATE_JOB_ID";

    // Signals only processes whose environment carries the job id; the kill script itself never has it.
    private const string KillJobScript =
        """for p in /proc/[0-9]*; do tr '\0' '\n' < "$p/environ" 2>/dev/null | grep -qxF -- "$1=$2" && kill -TERM "${p#/proc/}" 2>/dev/null; done; true""";

    // umask 077 keeps the copy and its directory private from the moment they are created.
    private const string CopySshKeyScript =
        "umask 077 && mkdir -p -- \"$(dirname -- \"$2\")\" && cp -- \"$1\" \"$2\" && chmod 600 -- \"$2\"";

    private const int KeyDirectoryHashLength = 12;

    private static readonly TimeSpan WslCommandTimeout = TimeSpan.FromSeconds(5);

    private readonly IWslAvailabilityProbe _availabilityProbe;
    private readonly SemaphoreSlim _availabilityGate = new(1, 1);
    private readonly bool _isRequired;
    private readonly ILogger<WslHelper> _logger;
    private bool? _wslAvailable;
    private bool? _isWsl2;
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

    public virtual bool RequiresWsl => _isRequired;

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
    ///     True when the default WSL distribution runs on WSL 2. Detected from the kernel release:
    ///     WSL 1 reports a fake "4.4.0-XXXXX-Microsoft" kernel, WSL 2 a real
    ///     "...-microsoft-standard-WSL2" one. Borg behaves differently on the two
    ///     (directory fsync support, SMB mount strategy), so callers branch on this.
    /// </summary>
    public async Task<bool> IsWsl2Async(CancellationToken ct = default)
    {
        if (!_isRequired)
            return false;

        if (_isWsl2 is { } cached)
            return cached;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(WslCommandTimeout);
        var result = await ProcessRunner.RunAsync("wsl", ["--exec", "uname", "-r"], timeout.Token);
        // Concurrent probes compute the same value, so an unsynchronized cache write is benign.
        _isWsl2 = result.ExitCode == 0 &&
                  result.StandardOutput.Contains("WSL2", StringComparison.OrdinalIgnoreCase);
        _logger.LogDebug("WSL kernel probe: exit={ExitCode}, kernel={Kernel}, isWsl2={IsWsl2}",
            result.ExitCode, result.StandardOutput.Trim(), _isWsl2);
        return _isWsl2.Value;
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
    ///     Builds the wsl.exe arguments that run a command inside WSL. The bash script is constant text
    ///     plus <paramref name="preCommands" />; the working directory, binary and arguments reach bash
    ///     only as positional parameters, so no value is ever parsed as shell syntax. <c>--exec</c> skips
    ///     the default shell of the distribution. Without a working directory the command starts in ~.
    ///     Environment variables travel separately through <see cref="ForwardEnvironment" />.
    /// </summary>
    public static List<string> WrapCommand(
        string binary, IReadOnlyList<string> arguments,
        string? workingDirectory = null,
        List<string>? preCommands = null)
    {
        var script = new List<string> { workingDirectory is not null ? "cd -- \"$1\" && shift" : "cd ~" };

        if (preCommands is { Count: > 0 })
            script.AddRange(preCommands);

        script.Add("\"$@\"");

        var wslArguments = new List<string> { "--exec", "bash", "-c", string.Join(" && ", script), "bash" };
        if (workingDirectory is not null)
            wslArguments.Add(workingDirectory);
        wslArguments.Add(binary);
        wslArguments.AddRange(arguments);
        return wslArguments;
    }

    /// <summary>
    ///     Returns the environment for a wsl.exe process that forwards <paramref name="variables" />
    ///     into Linux through WSLENV. Values stay in the process environment, which only the same
    ///     user can read, so secrets never reach a command line. Entries already listed in the
    ///     inherited WSLENV keep being forwarded.
    /// </summary>
    public static Dictionary<string, string> ForwardEnvironment(IReadOnlyDictionary<string, string> variables)
    {
        var environment = new Dictionary<string, string>(variables);
        var names = string.Join(':', variables.Keys);
        var inherited = Environment.GetEnvironmentVariable(WslEnvVariable);
        environment[WslEnvVariable] = string.IsNullOrEmpty(inherited) ? names : $"{inherited}:{names}";
        return environment;
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

    /// <summary>The key paths travel as positional parameters of a constant script.</summary>
    internal static IReadOnlyList<string> BuildCopySshKeyArguments(string wslSource, string wslDestination)
    {
        return ["--exec", "bash", "-c", CopySshKeyScript, "bash", wslSource, wslDestination];
    }

    /// <summary>
    ///     Copies a Windows SSH key into WSL with owner-only permissions. Each Windows key gets its own
    ///     file under <c>~/.ssh/borgmate</c>, so the copy never replaces a key of the WSL user or
    ///     another Windows key with the same file name.
    /// </summary>
    public virtual async Task<string?> CopySshKeyAsync(string windowsKeyPath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(windowsKeyPath) || !await IsAvailableAsync(ct))
            return null;

        var home = await GetWslHomeAsync(ct);
        if (home is null) return null;

        var wslSrc = ToWslPath(windowsKeyPath);
        var wslDest = WslKeyPath(home, windowsKeyPath);

        var psi = new ProcessStartInfo
        {
            FileName = "wsl",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in BuildCopySshKeyArguments(wslSrc, wslDest))
            psi.ArgumentList.Add(argument);

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
    public virtual async Task<string?> GetWslKeyPathAsync(string windowsKeyPath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(windowsKeyPath)) return null;
        if (!await IsAvailableAsync(ct)) return null;
        var home = await GetWslHomeAsync(ct);
        if (home is null) return null;
        return WslKeyPath(home, windowsKeyPath);
    }

    internal static string WslKeyPath(string home, string windowsKeyPath)
    {
        // Windows paths are case-insensitive, so differently cased spellings share one copy.
        var hash = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(windowsKeyPath.ToUpperInvariant())));
        var fileName = windowsKeyPath[(windowsKeyPath.LastIndexOfAny(['\\', '/']) + 1)..];
        return $"{home}/.ssh/borgmate/{hash[..KeyDirectoryHashLength]}-{fileName}";
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

    /// <summary>Returns a new id for <see cref="JobIdVariable" />, so cancelling one command spares the others.</summary>
    public static string NewJobId()
    {
        return Guid.NewGuid().ToString("N");
    }

    internal static IReadOnlyList<string> BuildKillJobArguments(string jobId)
    {
        return ["--exec", "bash", "-c", KillJobScript, "bash", JobIdVariable, jobId];
    }

    /// <summary>
    ///     Kills the Linux processes of the WSL command started with <paramref name="jobId" />. Called on
    ///     cancellation because Process.Kill on wsl.exe doesn't reach the Linux namespace.
    /// </summary>
    public static void KillJob(string jobId)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "wsl",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in BuildKillJobArguments(jobId))
            psi.ArgumentList.Add(argument);
        using var process = Process.Start(psi);
        process?.WaitForExit(3000);
    }

    [GeneratedRegex(@"^([A-Za-z]):[/\\](.*)$")]
    private static partial Regex DriveLetterRegex();
}
