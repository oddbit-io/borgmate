using System.Threading.Tasks;

namespace BorgMate.Runtime.Scheduling;

public class MockBackupScheduler : IBackupScheduler
{
    public void Start()
    {
    }

    public void Stop()
    {
    }

    public void RequestImmediateCheck()
    {
    }

    public Task RefreshWakeScheduleAsync(bool interactive)
    {
        return Task.CompletedTask;
    }
}
