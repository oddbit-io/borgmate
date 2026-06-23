using BorgMate.Domain.Repositories;
using BorgMate.Runtime.Orchestration;
using BorgMate.Runtime.Security;

namespace BorgMate.Tests.Runtime;

public class PassphraseProviderTests
{
    [Fact]
    public async Task EnsureAsync_ReturnsCachedPassphrase()
    {
        var repo = CreateEncryptedRepo();
        var provider = new PromptStubPassphraseProvider("cached");
        provider.Set(repo, "cached");

        var result = await provider.EnsureAsync(repo);

        Assert.True(result.IsT0);
        Assert.Equal("cached", result.AsT0.Passphrase);
    }

    [Fact]
    public async Task EnsureAsync_UsesPromptHookWhenPassphraseIsMissing()
    {
        var repo = CreateEncryptedRepo();
        var provider = new PromptStubPassphraseProvider("prompted");

        var result = await provider.EnsureAsync(repo);

        Assert.True(result.IsT0);
        Assert.Equal("prompted", result.AsT0.Passphrase);
        Assert.Equal("prompted", provider.GetCachedPassphrase(repo));
    }

    [Fact]
    public async Task RePromptAsync_ReplacesCachedPassphrase()
    {
        var repo = CreateEncryptedRepo();
        var provider = new PromptStubPassphraseProvider("replacement");
        provider.Set(repo, "old");

        var result = await ((IPassphraseProvider)provider).RePromptAsync(repo);

        Assert.True(result.IsT0);
        Assert.Equal("replacement", result.AsT0.Passphrase);
        Assert.Equal("replacement", provider.GetCachedPassphrase(repo));
    }

    [Fact]
    public async Task PromptCancellation_ReturnsCancelledOperationError()
    {
        var repo = CreateEncryptedRepo();
        var provider = new PromptStubPassphraseProvider(null);

        var result = await provider.EnsureAsync(repo);

        Assert.True(result.IsT1);
        Assert.IsType<OperationError.Cancelled>(result.AsT1);
    }

    [Fact]
    public void Clear_RemovesCachedPassphrase()
    {
        var repo = CreateEncryptedRepo();
        var provider = new PromptStubPassphraseProvider("secret");
        provider.Set(repo, "secret");

        provider.Clear(repo);

        Assert.Null(provider.GetCachedPassphrase(repo));
    }

    [Fact]
    public void MarkWrongAndIsWrong_TrackWrongPassphraseByRepositoryPath()
    {
        var repo = CreateEncryptedRepo();
        var provider = new PromptStubPassphraseProvider("secret");

        provider.MarkWrong(repo);

        Assert.True(provider.IsWrong(repo));
    }

    [Fact]
    public void Set_ClearsWrongPassphraseFlag()
    {
        var repo = CreateEncryptedRepo();
        var provider = new PromptStubPassphraseProvider("secret");
        provider.MarkWrong(repo);

        provider.Set(repo, "secret");

        Assert.False(provider.IsWrong(repo));
    }

    private static BorgRepository CreateEncryptedRepo() => new()
    {
        Name = "repo",
        Path = "/repo",
        EncryptionMode = BorgEncryptionMode.RepokeyBlake2
    };

    private sealed class PromptStubPassphraseProvider(string? promptedPassphrase) : PromptingPassphraseProvider(null)
    {
        public override Task<bool> EnsurePassphraseAsync(BorgRepository repo)
        {
            if (promptedPassphrase is null)
                return Task.FromResult(false);

            Set(repo, promptedPassphrase);
            return Task.FromResult(true);
        }

        public override Task<bool> RePromptAsync(BorgRepository repo)
        {
            if (promptedPassphrase is null)
                return Task.FromResult(false);

            Clear(repo);
            Set(repo, promptedPassphrase);
            return Task.FromResult(true);
        }
    }
}
