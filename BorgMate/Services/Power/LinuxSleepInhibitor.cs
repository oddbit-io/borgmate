using System.Diagnostics;
using System.IO;

namespace BorgMate.Services.Power;

/// <summary>
/// Linux sleep inhibitor. Spawns `systemd-inhibit ... sleep infinity` which holds
/// an inhibit lock on idle+sleep until the child is killed. Works on any
/// systemd-logind distro (GNOME, KDE, XFCE, etc.).
/// </summary>
internal class LinuxSleepInhibitor : ISleepInhibitor
{
    private Process? _inhibit;

    public void Inhibit(string reason)
    {
        if (_inhibit is { HasExited: false }) return;
        if (!File.Exists("/usr/bin/systemd-inhibit") && !File.Exists("/bin/systemd-inhibit")) return;

        try
        {
            _inhibit = Process.Start(new ProcessStartInfo
            {
                FileName = "systemd-inhibit",
                ArgumentList =
                {
                    "--what=idle:sleep",
                    "--mode=block",
                    "--who=BorgMate",
                    $"--why={reason}",
                    "sleep", "infinity",
                },
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
        }
        catch
        {
            _inhibit = null;
        }
    }

    public void Release()
    {
        var proc = _inhibit;
        _inhibit = null;
        if (proc is null) return;

        try
        {
            if (!proc.HasExited)
                proc.Kill(entireProcessTree: true);
        }
        catch { /* process may have already exited */ }
        finally { proc.Dispose(); }
    }
}
