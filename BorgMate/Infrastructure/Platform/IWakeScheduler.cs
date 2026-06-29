using System;
using System.Threading.Tasks;

namespace BorgMate.Infrastructure.Platform;

public interface IWakeScheduler
{
    event Action? StatusChanged;

    bool IsSupported { get; }
    string? LastError { get; }

    /// <param name="interactive">
    ///     True when invoked from a user-facing action (settings dialog) — GUI prompts are allowed.
    ///     Background refreshes must pass false so unattended runs never block on a prompt.
    /// </param>
    /// <param name="acOnly">
    ///     True to condition the wake/launch on AC power where the platform supports it
    ///     (Windows task conditions, systemd ConditionACPower; macOS enforces this in-app).
    /// </param>
    Task<WakeScheduleResult> ScheduleAsync(DateTime wakeAt, bool interactive, bool acOnly);

    /// <param name="interactive">See <see cref="ScheduleAsync" />.</param>
    Task<WakeScheduleResult> ClearAsync(bool interactive);
}
