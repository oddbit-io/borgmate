using System.Threading.Tasks;
using BorgMate.Models;
using BorgMate.Services.Borg;
using BorgMate.Services.Journal;
using BorgMate.Services.Keychain;
using BorgMate.Services.Queue;
using BorgMate.Services.UI;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace BorgMate.Tests;

public class BorgOperationRunnerTests
{
    private static readonly BorgResult SuccessResult = new(0, "ok", "");
    // stderr containing "passphrase" triggers WrongPassphrase classification
    private static readonly BorgResult WrongPassphraseResult = new(2, "", "wrong passphrase");
    private static readonly BorgResult GenericError = new(1, "", "some error");

    private static (BorgOperationRunner runner, PassphrasePrompt prompt) CreateRunner()
    {
        var prompt = Substitute.For<PassphrasePrompt>((IKeychainService?)null);
        prompt.EnsurePassphraseAsync(Arg.Any<BorgRepository>()).Returns(true);
        prompt.RePromptAsync(Arg.Any<BorgRepository>()).Returns(true);

        var runner = new BorgOperationRunner(
            Substitute.For<ILogger<BorgOperationRunner>>(),
            new JobQueueService(),
            Substitute.For<IJournalService>(),
            prompt);

        return (runner, prompt);
    }

    private static BorgRepository CreateRepo() => new()
    {
        Name = "test", Path = "/test",
        EncryptionMode = BorgEncryptionMode.RepokeyBlake2,
        Passphrase = "secret"
    };

    [Fact]
    public async Task PassphraseRetry_SuccessOnFirstAttempt_ReturnsSuccess()
    {
        var (runner, _) = CreateRunner();
        var result = await runner.RunWithPassphraseRetry(CreateRepo(), () => Task.FromResult(SuccessResult));
        Assert.True(result.Success);
    }

    [Fact]
    public async Task PassphraseRetry_NonPassphraseError_ReturnsError()
    {
        var (runner, _) = CreateRunner();
        var result = await runner.RunWithPassphraseRetry(CreateRepo(), () => Task.FromResult(GenericError));
        Assert.False(result.Success);
    }

    [Fact]
    public async Task PassphraseRetry_WrongThenSuccess_ReturnsSuccess()
    {
        var (runner, _) = CreateRunner();
        var attempt = 0;
        var result = await runner.RunWithPassphraseRetry(CreateRepo(), () =>
        {
            attempt++;
            return Task.FromResult(attempt == 1 ? WrongPassphraseResult : SuccessResult);
        });

        Assert.True(result.Success);
        Assert.Equal(2, attempt);
    }

    [Fact]
    public async Task PassphraseRetry_ThreeWrongAttempts_Fails()
    {
        var (runner, _) = CreateRunner();
        var repo = CreateRepo();
        var attempts = 0;

        var result = await runner.RunWithPassphraseRetry(repo, () =>
        {
            attempts++;
            return Task.FromResult(WrongPassphraseResult);
        });

        Assert.False(result.Success);
        Assert.Equal(3, attempts);
        Assert.True(repo.WrongPassphrase);
        Assert.Equal(string.Empty, repo.Passphrase);
    }

    [Fact]
    public async Task PassphraseRetry_InitialPromptDenied_ReturnsCancelled()
    {
        var (runner, prompt) = CreateRunner();
        prompt.EnsurePassphraseAsync(Arg.Any<BorgRepository>()).Returns(false);

        var repo = CreateRepo();
        var result = await runner.RunWithPassphraseRetry(repo, () => Task.FromResult(SuccessResult));

        Assert.False(result.Success);
        Assert.True(repo.WrongPassphrase);
    }

    [Fact]
    public async Task PassphraseRetry_RePromptDenied_ReturnsCancelled()
    {
        var (runner, prompt) = CreateRunner();
        prompt.RePromptAsync(Arg.Any<BorgRepository>()).Returns(false);

        var repo = CreateRepo();
        var result = await runner.RunWithPassphraseRetry(repo, () => Task.FromResult(WrongPassphraseResult));

        Assert.False(result.Success);
        Assert.True(repo.WrongPassphrase);
    }
}
