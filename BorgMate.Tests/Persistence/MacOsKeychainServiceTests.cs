namespace BorgMate.Tests.Persistence;

public class MacOsKeychainServiceTests
{
    [Fact]
    public async Task NonInteractiveRead_DisablesAuthorizationAndRestoresPolicy()
    {
        var service = new ControlledMacOsKeychainService();
        service.Read = () =>
        {
            Assert.False(service.InteractionAllowed);
            return "secret";
        };

        Assert.Equal("secret", await service.GetPassphraseAsync("/repo", false));
        Assert.True(service.InteractionAllowed);
    }

    [Fact]
    public async Task FailedRead_RestoresPolicy()
    {
        var service = new ControlledMacOsKeychainService
        {
            Read = () => throw new InvalidOperationException("native failure")
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetPassphraseAsync("/repo", false));

        Assert.True(service.InteractionAllowed);
    }

    [Fact]
    public async Task NonInteractiveRead_DoesNotWaitForAnotherInstancesAuthorizationDialog()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var interactive = new ControlledMacOsKeychainService
        {
            Read = () =>
            {
                entered.SetResult();
                Assert.True(release.Wait(TimeSpan.FromSeconds(10)));
                return "secret";
            }
        };
        var pending = interactive.GetPassphraseAsync("/interactive");
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var unattended = new ControlledMacOsKeychainService();

            Assert.Null(await unattended.GetPassphraseAsync("/scheduled", false).WaitAsync(TimeSpan.FromSeconds(1)));
        }
        finally
        {
            release.Set();
            await pending;
        }
    }
}
