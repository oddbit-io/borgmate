using BorgMate.Domain.Repositories;
using BorgMate.Persistence.Configuration;
using BorgMate.Runtime.Security;
using NSubstitute;

namespace BorgMate.Tests.Runtime;

public class ControlledRepositoryRelocationConfirmation : RepositoryRelocationConfirmation
{
    public ControlledRepositoryRelocationConfirmation(IRepositoryConfigStore? repositoryStore = null)
        : base(repositoryStore ?? Substitute.For<IRepositoryConfigStore>())
    {
    }

    public bool Approved { get; set; }
    public int PromptCount { get; private set; }

    protected override Task<bool> PromptAsync(string repositoryName, RepositoryRelocation relocation,
        CancellationToken ct)
    {
        PromptCount++;
        return Task.FromResult(Approved);
    }
}
