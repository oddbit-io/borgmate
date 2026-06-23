using System.Threading;
using System.Threading.Tasks;

namespace BorgMate.Infrastructure.Platform;

internal interface IWslAvailabilityProbe
{
    Task<bool> IsAvailableAsync(CancellationToken ct = default);
}
