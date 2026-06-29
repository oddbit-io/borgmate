using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace BorgMate.Infrastructure.Platform;

/// <summary>
///     Schedules a system wake via the Task Scheduler COM API (Schedule.Service).
///     COM is used instead of schtasks.exe so "task does not exist" is detectable by
///     HRESULT rather than by parsing localized console output.
/// </summary>
[SupportedOSPlatform("windows")]
internal class WindowsWakeScheduler : WakeSchedulerBase
{
    private const int TaskCreateOrUpdate = 6; // TASK_CREATE_OR_UPDATE
    private const int TaskLogonInteractiveToken = 3; // TASK_LOGON_INTERACTIVE_TOKEN
    private const int ErrorFileNotFound = unchecked((int)0x80070002);

    // Per-user task name: two accounts on the same machine must not clobber each other's wake.
    private static readonly string TaskName =
        "BorgMateScheduledWake-" + Environment.UserName.Replace('\\', '_').Replace('/', '_');

    public override bool IsSupported => true;

    protected override Task<WakeScheduleResult> ScheduleCoreAsync(DateTime wakeAt, bool interactive, bool acOnly)
    {
        var exePath = Environment.ProcessPath ?? "BorgMate.exe";
        var xml = CreateTaskDocument(exePath, wakeAt, acOnly).ToString();

        return Task.FromResult(WithRootFolder(folder =>
        {
            folder.RegisterTask(TaskName, xml, TaskCreateOrUpdate, null, null, TaskLogonInteractiveToken, null);
            return Ok();
        }));
    }

    protected override Task<WakeScheduleResult> ClearCoreAsync(bool interactive)
    {
        return Task.FromResult(WithRootFolder(folder =>
        {
            try
            {
                folder.DeleteTask(TaskName, 0);
            }
            catch (Exception ex) when (IsNotFound(ex))
            {
                // No task registered — nothing to clear.
            }

            return Ok();
        }));
    }

    private static WakeScheduleResult WithRootFolder(Func<dynamic, WakeScheduleResult> action)
    {
        var serviceType = Type.GetTypeFromProgID("Schedule.Service")
                          ?? throw new InvalidOperationException("Task Scheduler COM service is not available.");
        dynamic service = Activator.CreateInstance(serviceType)!;
        try
        {
            service.Connect();
            return action(service.GetFolder("\\"));
        }
        finally
        {
            Marshal.ReleaseComObject(service);
        }
    }

    private static bool IsNotFound(Exception ex)
    {
        return ex.HResult == ErrorFileNotFound ||
               (ex.InnerException is { } inner && IsNotFound(inner));
    }

    private static XDocument CreateTaskDocument(string exePath, DateTime wakeAt, bool acOnly)
    {
        XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
        return new XDocument(
            new XElement(ns + "Task",
                new XAttribute("version", "1.4"),
                new XElement(ns + "RegistrationInfo",
                    new XElement(ns + "Author", "BorgMate")),
                new XElement(ns + "Triggers",
                    new XElement(ns + "TimeTrigger",
                        new XElement(ns + "StartBoundary",
                            wakeAt.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture)),
                        new XElement(ns + "Enabled", "true"))),
                new XElement(ns + "Principals",
                    new XElement(ns + "Principal",
                        new XAttribute("id", "Author"),
                        new XElement(ns + "LogonType", "InteractiveToken"),
                        new XElement(ns + "RunLevel", "LeastPrivilege"))),
                new XElement(ns + "Settings",
                    new XElement(ns + "MultipleInstancesPolicy", "IgnoreNew"),
                    new XElement(ns + "DisallowStartIfOnBatteries", acOnly ? "true" : "false"),
                    new XElement(ns + "StopIfGoingOnBatteries", acOnly ? "true" : "false"),
                    new XElement(ns + "AllowHardTerminate", "true"),
                    // false: a wake missed because the PC was powered off must not
                    // surprise-launch BorgMate at the next logon.
                    new XElement(ns + "StartWhenAvailable", "false"),
                    new XElement(ns + "WakeToRun", "true"),
                    new XElement(ns + "Enabled", "true"),
                    new XElement(ns + "Hidden", "true"),
                    // PT0S = unlimited: the launched process becomes the long-lived tray
                    // app; a time limit would hard-kill it (and any running backup).
                    new XElement(ns + "ExecutionTimeLimit", "PT0S")),
                new XElement(ns + "Actions",
                    new XAttribute("Context", "Author"),
                    new XElement(ns + "Exec",
                        new XElement(ns + "Command", exePath),
                        new XElement(ns + "Arguments", "--scheduled-wake")))));
    }
}
