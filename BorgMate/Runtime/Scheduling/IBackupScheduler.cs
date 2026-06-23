namespace BorgMate.Runtime.Scheduling;

/// <summary>Checks scheduled repos every minute and triggers backups when due.</summary>
public interface IBackupScheduler
{
    void Start();
    void Stop();
}