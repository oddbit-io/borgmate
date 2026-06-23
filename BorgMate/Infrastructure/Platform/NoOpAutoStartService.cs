namespace BorgMate.Infrastructure.Platform;

internal class NoOpAutoStartService : IAutoStartService
{
    public void SetEnabled(bool enabled)
    {
    }

    public bool IsEnabled()
    {
        return false;
    }
}