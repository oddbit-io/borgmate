using BorgMate.ViewModels;

namespace BorgMate.Services;

/// <summary>
/// Checks scheduled repositories every minute and triggers backups when due.
/// </summary>
public interface ISchedulerService
{
    void Start(RepositoryListViewModel repoList);
    void Stop();
}
