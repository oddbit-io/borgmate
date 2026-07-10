using BorgMate.Domain.Repositories;
using BorgMate.Infrastructure.Platform;
using BorgMate.Persistence.Keychain;
using BorgMate.Runtime.Orchestration;
using BorgMate.Runtime.Security;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using OneOf;

namespace BorgMate.Tests.Infrastructure;

public class NetworkShareMounterBaseTests
{
    [Fact]
    public void OperationsAreInteractiveByDefault()
    {
        Assert.True(OperationInteractivity.IsInteractive);
    }

    [Fact]
    public async Task EnsureMounted_NonInteractive_WithoutStoredPassword_FailsInsteadOfPrompting()
    {
        var mounter = new FakeMounter(keychain: null);
        OperationInteractivity.IsInteractive = false;

        await Assert.ThrowsAsync<CredentialsRequiredException>(
            () => mounter.EnsureMountedAsync(Location));

        Assert.Equal(0, mounter.MountAttempts);
    }

    [Fact]
    public async Task EnsureMounted_NonInteractive_WithStoredPassword_Mounts()
    {
        var keychain = Substitute.For<ICredentialStore>();
        keychain.GetPassphraseAsync(Location.KeychainKey).Returns("stored");
        var mounter = new FakeMounter(keychain);
        OperationInteractivity.IsInteractive = false;

        var mountPoint = await mounter.EnsureMountedAsync(Location);

        Assert.Equal("/mnt/share", mountPoint);
        Assert.Equal("stored", mounter.LastPassword);
    }

    private static SmbLocation Location => new()
    {
        Host = "nas",
        Share = "backups",
        Username = "user"
    };

    private sealed class FakeMounter(ICredentialStore? keychain)
        : NetworkShareMounterBase(NullLogger.Instance, keychain)
    {
        public int MountAttempts { get; private set; }
        public string? LastPassword { get; private set; }

        protected override Task<string?> TryGetExistingMountPointAsync(
            SmbLocation location, CancellationToken ct)
        {
            return Task.FromResult<string?>(null);
        }

        protected override Task<OneOf<MountedShare, MountError>> MountOnceAsync(
            SmbLocation location, string? password, CancellationToken ct)
        {
            MountAttempts++;
            LastPassword = password;
            return Task.FromResult<OneOf<MountedShare, MountError>>(new MountedShare("/mnt/share"));
        }
    }
}
