using System.Threading;
using System.Threading.Tasks;
using BorgMate.Infrastructure.Platform;

namespace BorgMate.Tests.Infrastructure;

internal class ControllableWslAvailabilityProbe : IWslAvailabilityProbe
{
    private readonly TaskCompletionSource<bool> _completion = new();

    public int Calls { get; private set; }

    public Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        Calls++;
        return _completion.Task.WaitAsync(ct);
    }

    public void Complete(bool isAvailable)
    {
        _completion.SetResult(isAvailable);
    }
}
