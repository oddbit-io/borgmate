using System.Threading.Tasks;

namespace BorgMate.Infrastructure.Platform;

public interface IPowerSource
{
    /// <summary>
    ///     True when the machine is currently drawing from battery.
    ///     Implementations fail open (false) so a probe error never silently skips backups.
    /// </summary>
    Task<bool> IsOnBatteryAsync();
}
