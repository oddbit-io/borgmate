using System;
using System.Threading.Tasks;

namespace BorgMate.Infrastructure.Platform;

/// <summary>
///     Shared status bookkeeping for wake schedulers. The public methods guarantee
///     "never throws": any exception from a platform implementation (file I/O, COM, …)
///     becomes a Failed result and updates <see cref="LastError" />/<see cref="StatusChanged" />.
/// </summary>
internal abstract class WakeSchedulerBase : IWakeScheduler
{
    public event Action? StatusChanged;

    public abstract bool IsSupported { get; }
    public string? LastError { get; private set; }

    public async Task<WakeScheduleResult> ScheduleAsync(DateTime wakeAt, bool interactive, bool acOnly)
    {
        try
        {
            return await ScheduleCoreAsync(wakeAt, interactive, acOnly);
        }
        catch (Exception ex)
        {
            return Fail(ex.Message);
        }
    }

    public async Task<WakeScheduleResult> ClearAsync(bool interactive)
    {
        try
        {
            return await ClearCoreAsync(interactive);
        }
        catch (Exception ex)
        {
            return Fail(ex.Message);
        }
    }

    protected abstract Task<WakeScheduleResult> ScheduleCoreAsync(DateTime wakeAt, bool interactive, bool acOnly);
    protected abstract Task<WakeScheduleResult> ClearCoreAsync(bool interactive);

    protected WakeScheduleResult Ok()
    {
        LastError = null;
        StatusChanged?.Invoke();
        return WakeScheduleResult.Ok();
    }

    protected WakeScheduleResult Fail(string error)
    {
        LastError = error;
        StatusChanged?.Invoke();
        return WakeScheduleResult.Failed(error);
    }
}
