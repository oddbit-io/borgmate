namespace BorgMate.Infrastructure.Platform;

/// <summary>
///     Prevents the system from sleeping while a long-running borg command is active.
///     Platform implementations: macOS (caffeinate), Linux (systemd-inhibit), Windows (SetThreadExecutionState).
///     Calls must be paired — every Inhibit() should eventually be followed by Release().
///     Implementations are idempotent: repeated Inhibit() or Release() calls are safe.
/// </summary>
public interface ISleepInhibitor
{
    void Inhibit(string reason);
    void Release();
}