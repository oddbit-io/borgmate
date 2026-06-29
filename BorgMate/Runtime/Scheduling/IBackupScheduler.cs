using System.Threading.Tasks;

namespace BorgMate.Runtime.Scheduling;

/// <summary>Checks scheduled repos every minute and triggers backups when due.</summary>
public interface IBackupScheduler
{
    void Start();
    void Stop();

    /// <summary>Runs a schedule check now instead of waiting for the next timer tick.</summary>
    void RequestImmediateCheck();

    /// <summary>
    ///     Recomputes and applies the OS wake schedule. Pass interactive=true only for
    ///     user-initiated calls — it allows platform GUI prompts (e.g. the macOS admin prompt).
    /// </summary>
    Task RefreshWakeScheduleAsync(bool interactive);
}
