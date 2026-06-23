using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace BorgMate.Infrastructure.Platform;

/// <summary>
///     Windows sleep inhibitor. Uses SetThreadExecutionState with ES_CONTINUOUS
///     so the effect persists until Release() is called (or the calling thread
///     terminates, which happens automatically on app exit).
///     Must be called from a stable long-lived thread (the UI thread).
/// </summary>
[SupportedOSPlatform("windows")]
internal class WindowsSleepInhibitor : ISleepInhibitor
{
    public void Inhibit(string reason)
    {
        try
        {
            SetThreadExecutionState(ExecutionState.Continuous | ExecutionState.SystemRequired);
        }
        catch
        {
            /* best effort */
        }
    }

    public void Release()
    {
        try
        {
            SetThreadExecutionState(ExecutionState.Continuous);
        }
        catch
        {
            /* best effort */
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern ExecutionState SetThreadExecutionState(ExecutionState flags);

    [Flags]
    private enum ExecutionState : uint
    {
        Continuous = 0x80000000,
        SystemRequired = 0x00000001
    }
}