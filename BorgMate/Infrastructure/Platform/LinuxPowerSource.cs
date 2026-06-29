using System.IO;
using System.Threading.Tasks;

namespace BorgMate.Infrastructure.Platform;

internal class LinuxPowerSource : IPowerSource
{
    public Task<bool> IsOnBatteryAsync()
    {
        try
        {
            var mainsSeen = false;
            foreach (var directory in Directory.EnumerateDirectories("/sys/class/power_supply"))
            {
                var typePath = Path.Combine(directory, "type");
                if (!File.Exists(typePath) || File.ReadAllText(typePath).Trim() != "Mains")
                    continue;

                mainsSeen = true;
                var onlinePath = Path.Combine(directory, "online");
                if (File.Exists(onlinePath) && File.ReadAllText(onlinePath).Trim() == "1")
                    return Task.FromResult(false); // an AC adapter is online
            }

            // Mains adapters exist but none online → battery; no Mains at all → desktop, AC.
            return Task.FromResult(mainsSeen);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }
}
