namespace BorgMate.Infrastructure.Platform;

internal class NoOpSleepInhibitor : ISleepInhibitor
{
    public void Inhibit(string reason)
    {
    }

    public void Release()
    {
    }
}