using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading.Tasks;

namespace BorgMate.Infrastructure.Platform;

[SupportedOSPlatform("windows")]
internal class WindowsPowerSource : IPowerSource
{
    public Task<bool> IsOnBatteryAsync()
    {
        // ACLineStatus: 0 = offline (battery), 1 = online, 255 = unknown (fail open).
        return Task.FromResult(GetSystemPowerStatus(out var status) && status.ACLineStatus == 0);
    }

    [DllImport("kernel32.dll")]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }
}
