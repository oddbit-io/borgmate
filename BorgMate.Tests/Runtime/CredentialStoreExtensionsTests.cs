using BorgMate.Infrastructure.Notifications;
using BorgMate.Persistence.Keychain;
using BorgMate.Runtime.Security;
using NSubstitute;

namespace BorgMate.Tests.Runtime;

public class CredentialStoreExtensionsTests
{
    [Fact]
    public async Task TrySetPassphrase_NotifiesUser_WhenStoreRejects()
    {
        var store = Substitute.For<ICredentialStore>();
        store.SetPassphraseAsync("/repo", "secret").Returns(false);
        var notifier = Substitute.For<INotificationService>();

        var ok = await store.TrySetPassphraseAsync(notifier, "/repo", "secret");

        Assert.False(ok);
        notifier.Received(1).Send(Arg.Any<string>(), Arg.Is<string>(body => body.Length > 0));
    }

    [Fact]
    public async Task TrySetPassphrase_DoesNotNotify_WhenStoreSucceeds()
    {
        var store = Substitute.For<ICredentialStore>();
        store.SetPassphraseAsync("/repo", "secret").Returns(true);
        var notifier = Substitute.For<INotificationService>();

        var ok = await store.TrySetPassphraseAsync(notifier, "/repo", "secret");

        Assert.True(ok);
        notifier.DidNotReceive().Send(Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task TrySetPassphrase_ToleratesMissingAndThrowingNotifier()
    {
        var store = Substitute.For<ICredentialStore>();
        store.SetPassphraseAsync("/repo", "secret").Returns(false);

        Assert.False(await store.TrySetPassphraseAsync(null, "/repo", "secret"));

        var throwing = Substitute.For<INotificationService>();
        throwing.When(n => n.Send(Arg.Any<string>(), Arg.Any<string>()))
            .Do(_ => throw new InvalidOperationException("no notification daemon"));
        Assert.False(await store.TrySetPassphraseAsync(throwing, "/repo", "secret"));
    }
}
