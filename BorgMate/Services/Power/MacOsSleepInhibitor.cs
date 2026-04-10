using System;
using System.Diagnostics;

namespace BorgMate.Services.Power;

/// <summary>
/// macOS sleep inhibitor. Spawns `caffeinate -i -w &lt;ppid&gt;` which prevents
/// idle system sleep and auto-exits if BorgMate crashes (no orphan leak).
/// </summary>
internal class MacOsSleepInhibitor : ISleepInhibitor
{
    private Process? _caffeinate;

    public void Inhibit(string reason)
    {
        if (_caffeinate is { HasExited: false }) return;

        try
        {
            var parentPid = Environment.ProcessId;
            _caffeinate = Process.Start(new ProcessStartInfo
            {
                FileName = "caffeinate",
                // -i: prevent idle sleep; -w <pid>: exit when our process exits
                Arguments = $"-i -w {parentPid}",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
        }
        catch
        {
            // Best-effort: sleep inhibition failing shouldn't break the app.
            _caffeinate = null;
        }
    }

    public void Release()
    {
        var proc = _caffeinate;
        _caffeinate = null;
        if (proc is null) return;

        try
        {
            if (!proc.HasExited)
                proc.Kill();
        }
        catch { /* process may have already exited */ }
        finally { proc.Dispose(); }
    }
}
