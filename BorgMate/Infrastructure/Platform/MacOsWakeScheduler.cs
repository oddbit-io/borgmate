using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using BorgMate.Infrastructure.Borg;

namespace BorgMate.Infrastructure.Platform;

/// <summary>
///     Schedules a system wake via pmset (root required) plus a LaunchAgent that starts
///     BorgMate at the wake time. Passwordless pmset access is granted once, interactively,
///     through a narrow /etc/sudoers.d rule — background reschedules then run unattended
///     via `sudo -n` with no prompts.
/// </summary>
internal class MacOsWakeScheduler : WakeSchedulerBase
{
    private const string Label = "com.borgmate.scheduledwake";
    private const string Owner = "BorgMate";
    private const string PmsetPath = "/usr/bin/pmset";
    private const string SudoersPath = "/etc/sudoers.d/borgmate";

    private static readonly TimeSpan ProcessTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan PromptTimeout = TimeSpan.FromMinutes(5);

    private static readonly Regex BorgMateSchedulePattern = new(
        @"\]\s+(?<type>\w+)\s+at\s+(?<date>\d{2}/\d{2}/\d{4}\s+\d{2}:\d{2}:\d{2})\s+by\s+'BorgMate'",
        RegexOptions.Compiled);

    private static readonly Regex SafeUserNamePattern = new("^[A-Za-z0-9._-]+$", RegexOptions.Compiled);

    public override bool IsSupported => File.Exists(PmsetPath);

    private static string LaunchAgentPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Library", "LaunchAgents", Label + ".plist");

    // acOnly is not reflected in pmset/launchd — neither supports a power-source condition.
    // The AC-only gate is enforced in App at the scheduled-wake entry points instead.
    protected override async Task<WakeScheduleResult> ScheduleCoreAsync(DateTime wakeAt, bool interactive, bool acOnly)
    {
        if (!IsSupported)
            return Fail("pmset is not available; BorgMate cannot schedule macOS wake events.");

        var pmset = await SchedulePmsetAsync(wakeAt, interactive);
        if (!pmset.IsSuccess) return pmset;

        return await InstallLaunchAgentAsync(wakeAt);
    }

    protected override async Task<WakeScheduleResult> ClearCoreAsync(bool interactive)
    {
        WakeScheduleResult? failure = null;
        if (IsSupported)
        {
            var existing = await GetScheduledWakesAsync();
            if (existing.Count > 0)
                failure = interactive
                    ? await CancelAllPrivilegedAsync(existing)
                    : await CancelAllUnattendedAsync(existing);
        }

        await UnloadLaunchAgentAsync();
        if (File.Exists(LaunchAgentPath))
            File.Delete(LaunchAgentPath);

        return failure is { IsSuccess: false } ? failure : Ok();
    }

    private async Task<WakeScheduleResult> SchedulePmsetAsync(DateTime wakeAt, bool interactive)
    {
        var existing = await GetScheduledWakesAsync();
        if (existing.Contains(wakeAt))
            return Ok();

        foreach (var stale in existing)
        {
            var cancel = await RunPmsetAsync(interactive, CancelArguments(stale));
            if (!cancel.IsSuccess) return cancel;
        }

        return await RunPmsetAsync(interactive, ["schedule", "wakeorpoweron", FormatPmsetDate(wakeAt), Owner]);
    }

    /// <summary>
    ///     Runs `sudo -n pmset …`. When sudo denies (no sudoers rule yet) and the call is
    ///     interactive, installs the rule via a single admin prompt and retries.
    /// </summary>
    private async Task<WakeScheduleResult> RunPmsetAsync(bool interactive, string[] pmsetArguments)
    {
        var result = await RunProcessAsync("sudo", ["-n", PmsetPath, .. pmsetArguments], ProcessTimeout);
        if (result.Success)
            return Ok();

        if (!IsSudoDenied(result))
            return Fail(ErrorText(result));

        if (!interactive)
            return Fail(Strings.Get("WakeForScheduledBackups.PermissionRequired"));

        var install = await InstallSudoersRuleAsync();
        if (!install.IsSuccess) return install;

        result = await RunProcessAsync("sudo", ["-n", PmsetPath, .. pmsetArguments], ProcessTimeout);
        return result.Success ? Ok() : Fail(ErrorText(result));
    }

    private async Task<WakeScheduleResult> InstallSudoersRuleAsync()
    {
        var user = Environment.UserName;
        if (!SafeUserNamePattern.IsMatch(user))
            return Fail($"Cannot grant pmset permission for user name '{user}'.");

        // Narrow rule: only the exact pmset schedule forms BorgMate issues, anchored by owner.
        var rule = $"{user} ALL=(root) NOPASSWD: {PmsetPath} schedule wakeorpoweron * {Owner}, " +
                   $"{PmsetPath} schedule cancel wakeorpoweron * {Owner}\n";

        var tempPath = Path.Combine(Path.GetTempPath(), "borgmate-sudoers");
        File.WriteAllText(tempPath, rule);
        try
        {
            var command = "/usr/sbin/visudo -c -f " + ShellQuote(tempPath) +
                          " && /usr/bin/install -o root -g wheel -m 0440 " +
                          ShellQuote(tempPath) + " " + SudoersPath;
            return await RunPrivilegedShellAsync(command);
        }
        finally
        {
            try
            {
                File.Delete(tempPath);
            }
            catch
            {
                /* temp cleanup */
            }
        }
    }

    /// <summary>Cancels all events and removes the sudoers rule in one admin prompt.</summary>
    private async Task<WakeScheduleResult> CancelAllPrivilegedAsync(IReadOnlyList<DateTime> wakes)
    {
        var commands = wakes
            .Select(wake =>
            {
                string[] argv = [PmsetPath, .. CancelArguments(wake)];
                return string.Join(" ", argv.Select(ShellQuote));
            })
            .Append("/bin/rm -f " + SudoersPath);
        return await RunPrivilegedShellAsync(string.Join("; ", commands));
    }

    private async Task<WakeScheduleResult> CancelAllUnattendedAsync(IReadOnlyList<DateTime> wakes)
    {
        foreach (var wake in wakes)
        {
            var cancel = await RunPmsetAsync(false, CancelArguments(wake));
            if (!cancel.IsSuccess) return cancel;
        }

        return Ok();
    }

    private static string[] CancelArguments(DateTime wake)
    {
        // pmset -g sched prints the raw event type ("wakepoweron"), but the cancel parser
        // only accepts input tokens — always cancel with the canonical "wakeorpoweron".
        return ["schedule", "cancel", "wakeorpoweron", FormatPmsetDate(wake), Owner];
    }

    private static async Task<IReadOnlyList<DateTime>> GetScheduledWakesAsync()
    {
        var result = await RunProcessAsync(PmsetPath, ["-g", "sched"], ProcessTimeout);
        if (!result.Success || string.IsNullOrWhiteSpace(result.StandardOutput))
            return [];

        return BorgMateSchedulePattern.Matches(result.StandardOutput)
            .Where(match => match.Groups["type"].Value.Contains("wake", StringComparison.OrdinalIgnoreCase))
            .Select(match => DateTime.ParseExact(
                match.Groups["date"].Value,
                "MM/dd/yyyy HH:mm:ss",
                CultureInfo.InvariantCulture))
            .ToArray();
    }

    private async Task<WakeScheduleResult> InstallLaunchAgentAsync(DateTime wakeAt)
    {
        var launchAgentDirectory = Path.GetDirectoryName(LaunchAgentPath);
        if (launchAgentDirectory is null)
            return Fail("Could not resolve the LaunchAgents directory.");

        var content = CreateLaunchAgentDocument(wakeAt).ToString();
        if (File.Exists(LaunchAgentPath) && File.ReadAllText(LaunchAgentPath) == content)
            return Ok(); // unchanged — skip the launchctl churn

        Directory.CreateDirectory(launchAgentDirectory);
        File.WriteAllText(LaunchAgentPath, content);
        await UnloadLaunchAgentAsync();

        var load = await RunProcessAsync("launchctl", ["load", "-w", LaunchAgentPath], ProcessTimeout);
        return load.Success ? Ok() : Fail(ErrorText(load));
    }

    private static async Task UnloadLaunchAgentAsync()
    {
        if (File.Exists(LaunchAgentPath))
            await RunProcessAsync("launchctl", ["unload", LaunchAgentPath], ProcessTimeout);
    }

    private static XDocument CreateLaunchAgentDocument(DateTime wakeAt)
    {
        var arguments = GetProgramArguments();
        return new XDocument(
            new XDocumentType("plist", "-//Apple//DTD PLIST 1.0//EN",
                "http://www.apple.com/DTDs/PropertyList-1.0.dtd", null),
            new XElement("plist",
                new XAttribute("version", "1.0"),
                new XElement("dict",
                    Key("Label"), new XElement("string", Label),
                    Key("ProgramArguments"),
                    new XElement("array", arguments.Select(argument => new XElement("string", argument))),
                    Key("StartCalendarInterval"),
                    new XElement("dict",
                        Key("Month"), new XElement("integer", wakeAt.Month),
                        Key("Day"), new XElement("integer", wakeAt.Day),
                        Key("Hour"), new XElement("integer", wakeAt.Hour),
                        Key("Minute"), new XElement("integer", wakeAt.Minute)),
                    Key("RunAtLoad"), new XElement("false"))));
    }

    private static string[] GetProgramArguments()
    {
        var appPath = MacOsAutoStartService.GetAppPath();
        if (appPath.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
            return ["/usr/bin/open", "-gj", "-a", appPath, "--args", "--scheduled-wake"];

        return [appPath, "--scheduled-wake"];
    }

    private static XElement Key(string value)
    {
        return new XElement("key", value);
    }

    private async Task<WakeScheduleResult> RunPrivilegedShellAsync(string command)
    {
        var script = "do shell script " + AppleScriptQuote(command) + " with administrator privileges";
        var result = await RunProcessAsync("osascript", ["-e", script], PromptTimeout);
        return result.Success ? Ok() : Fail(ErrorText(result));
    }

    private static async Task<BorgResult> RunProcessAsync(
        string fileName, IReadOnlyList<string> arguments, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        return await ProcessRunner.RunAsync(fileName, arguments, cts.Token);
    }

    private static bool IsSudoDenied(BorgResult result)
    {
        // sudo's own denial messages are prefixed "sudo:"; pmset errors pass through as-is.
        return !result.WasCancelled &&
               result.StandardError.TrimStart().StartsWith("sudo:", StringComparison.Ordinal);
    }

    private static string ErrorText(BorgResult result)
    {
        if (result.WasCancelled)
            return "Timed out while waiting for the command to finish.";

        var error = result.StandardError.Trim();
        return string.IsNullOrWhiteSpace(error) ? result.StandardOutput.Trim() : error;
    }

    private static string FormatPmsetDate(DateTime value)
    {
        return value.ToString("MM/dd/yy HH:mm:ss", CultureInfo.InvariantCulture);
    }

    private static string ShellQuote(string value)
    {
        return "'" + value.Replace("'", "'\\''") + "'";
    }

    private static string AppleScriptQuote(string value)
    {
        return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }
}
