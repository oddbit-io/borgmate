using BorgMate.Domain.Repositories;
using BorgMate.Persistence.Configuration;
using BorgMate.Runtime.Orchestration;
using BorgMate.Runtime.Progress;
using NSubstitute;

namespace BorgMate.Tests.Runtime;

public class RepositoryRelocationConfirmationTests
{
    private static RepositoryRelocation Relocation => new() { PreviousLocation = "/old", Location = "/new" };

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Approval_PersistsOnlySavedRepositories(bool persist)
    {
        var store = Substitute.For<IRepositoryConfigStore>();
        var confirmation = new ControlledRepositoryRelocationConfirmation(store) { Approved = true };
        var repo = new BorgRepository { Path = "/new" };
        var request = new BorgOperationRequest(repo, NullOperationProgressSink.Instance, PersistRelocations: persist);

        Assert.True(await confirmation.ConfirmAsync(request, Relocation, CancellationToken.None));

        Assert.Equal(Relocation, Assert.Single(repo.ConfirmedRelocations));
        Assert.Equal(1, confirmation.PromptCount);
        await store.Received(persist ? 1 : 0).UpdateAsync(repo, Arg.Any<CancellationToken>());
        await store.Received(persist ? 1 : 0).SaveAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task NonInteractive_UsesOnlyPreviouslyConfirmedRelocations(bool known)
    {
        var confirmation = new ControlledRepositoryRelocationConfirmation { Approved = true };
        var repo = new BorgRepository { ConfirmedRelocations = known ? [Relocation] : [] };
        var request = new BorgOperationRequest(repo, NullOperationProgressSink.Instance, Interactive: false);

        Assert.Equal(known, await confirmation.ConfirmAsync(request, Relocation, CancellationToken.None));
        Assert.Equal(0, confirmation.PromptCount);
    }

    [Fact]
    public async Task DeclinedRelocation_IsNotRememberedOrSaved()
    {
        var store = Substitute.For<IRepositoryConfigStore>();
        var confirmation = new ControlledRepositoryRelocationConfirmation(store);
        var repo = new BorgRepository();

        Assert.False(await confirmation.ConfirmAsync(
            new BorgOperationRequest(repo, NullOperationProgressSink.Instance), Relocation, CancellationToken.None));

        Assert.Empty(repo.ConfirmedRelocations);
        await store.DidNotReceiveWithAnyArgs().SaveAsync(Arg.Any<CancellationToken>());
    }
}
