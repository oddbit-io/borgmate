using BorgMate.ViewModels;

namespace BorgMate.Services;

/// <summary>Checks scheduled repos every minute and triggers backups when due.</summary>
public interface ISchedulerService
{
    void Start(RepositoriesPageViewModel page);
    void Stop();
}
