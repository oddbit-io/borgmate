using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BorgMate.Infrastructure.Borg;

namespace BorgMate.Infrastructure.Platform;

internal class LinuxWakeScheduler : WakeSchedulerBase
{
    private const string ServiceName = "borgmate-wake.service";
    private const string TimerName = "borgmate-wake.timer";
    private static readonly TimeSpan SystemctlTimeout = TimeSpan.FromSeconds(10);

    public override bool IsSupported =>
        File.Exists("/usr/bin/systemctl") || File.Exists("/bin/systemctl");

    private static string UserSystemdDirectory
    {
        get
        {
            var configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            if (string.IsNullOrWhiteSpace(configHome))
                configHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
            return Path.Combine(configHome, "systemd", "user");
        }
    }

    private static string ServicePath => Path.Combine(UserSystemdDirectory, ServiceName);
    private static string TimerPath => Path.Combine(UserSystemdDirectory, TimerName);

    protected override async Task<WakeScheduleResult> ScheduleCoreAsync(DateTime wakeAt, bool interactive, bool acOnly)
    {
        if (!IsSupported)
            return Fail("systemctl is not available; BorgMate cannot install a user wake timer.");

        Directory.CreateDirectory(UserSystemdDirectory);
        File.WriteAllText(ServicePath, CreateServiceFile(acOnly));
        File.WriteAllText(TimerPath, CreateTimerFile(wakeAt));

        var reload = await RunSystemctlAsync("daemon-reload");
        if (!reload.IsSuccess) return reload;

        var enable = await RunSystemctlAsync("enable", TimerName);
        if (!enable.IsSuccess) return enable;

        return await RunSystemctlAsync("restart", TimerName);
    }

    protected override async Task<WakeScheduleResult> ClearCoreAsync(bool interactive)
    {
        if (!File.Exists(TimerPath) && !File.Exists(ServicePath))
            return Ok(); // nothing installed — don't spawn systemctl on every startup

        WakeScheduleResult? failure = null;
        if (IsSupported)
        {
            var disable = await RunSystemctlAsync("disable", "--now", TimerName);
            if (!disable.IsSuccess) failure = disable;
        }

        DeleteIfExists(TimerPath);
        DeleteIfExists(ServicePath);

        if (IsSupported)
        {
            var reload = await RunSystemctlAsync("daemon-reload");
            if (!reload.IsSuccess) failure ??= reload;
        }

        return failure ?? Ok();
    }

    private static string CreateServiceFile(bool acOnly)
    {
        var execStart = string.Join(" ", GetProgramArguments().Select(EscapeSystemdArgument));
        // The wake alarm itself (WakeSystem) can't be conditioned: on battery the machine
        // wakes briefly, the service is skipped, and the system goes back to sleep.
        var condition = acOnly ? "ConditionACPower=true\n" : string.Empty;
        return $"""
                [Unit]
                Description=BorgMate scheduled wake runner
                {condition}
                [Service]
                Type=exec
                ExecStart={execStart}
                """;
    }

    private static string[] GetProgramArguments()
    {
        return [LinuxAutoStartService.GetLauncherPath(), "--scheduled-wake"];
    }

    private static string CreateTimerFile(DateTime wakeAt)
    {
        var onCalendar = wakeAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        return $"""
                [Unit]
                Description=Wake computer for BorgMate scheduled backup

                [Timer]
                OnCalendar={onCalendar}
                WakeSystem=true
                AccuracySec=1min
                Persistent=false
                Unit={ServiceName}

                [Install]
                WantedBy=timers.target
                """;
    }

    private async Task<WakeScheduleResult> RunSystemctlAsync(params string[] arguments)
    {
        using var cts = new CancellationTokenSource(SystemctlTimeout);
        var result = await ProcessRunner.RunAsync("systemctl", ["--user", .. arguments], cts.Token);

        if (result.WasCancelled)
            return Fail("Timed out while running systemctl --user.");
        if (result.Success)
            return Ok();

        var error = string.IsNullOrWhiteSpace(result.StandardError)
            ? result.StandardOutput.Trim()
            : result.StandardError.Trim();

        // A --user manager without CAP_WAKE_ALARM (systemd < 254) cannot arm wake timers.
        if (error.Contains("Operation not permitted", StringComparison.OrdinalIgnoreCase))
            error += " " + Strings.Get("WakeForScheduledBackups.LinuxWakeHint");

        return Fail(error);
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }

    private static string EscapeSystemdArgument(string value)
    {
        // On top of shell-style escaping, systemd expands % specifiers and $ variables
        // in ExecStart even inside double quotes.
        return "\"" + StringHelpers.EscapeShell(value).Replace("%", "%%").Replace("$", "$$") + "\"";
    }
}
