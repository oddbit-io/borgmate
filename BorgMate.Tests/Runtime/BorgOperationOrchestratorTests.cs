using BorgMate.Domain.Journal;
using BorgMate.Domain.Repositories;
using BorgMate.Infrastructure.Borg;
using BorgMate.Persistence.Journal;
using BorgMate.Runtime.Jobs;
using BorgMate.Runtime.Operations;
using BorgMate.Runtime.Orchestration;
using BorgMate.Runtime.Progress;
using BorgMate.Runtime.Security;
using Microsoft.Extensions.Logging;
using NSubstitute;
using OneOf;
using JournalEntry = BorgMate.Domain.Journal.JournalEntry;

namespace BorgMate.Tests.Runtime;

public class BorgOperationOrchestratorTests
{
    private static readonly BorgResult SuccessResult = new(0, "ok", "");
    private static readonly BorgResult WrongPassphraseResult = new(
        (int)BorgModernExitCode.PassphraseWrong,
        "",
        "wrong passphrase");
    private static readonly BorgResult GenericError = new((int)BorgModernExitCode.Error, "", "some error");

    private static (BorgOperationOrchestrator orchestrator, IPassphraseProvider provider) CreateOrchestrator()
    {
        var provider = Substitute.For<IPassphraseProvider>();
        provider.EnsureAsync(Arg.Any<BorgRepository>(), Arg.Any<CancellationToken>())
            .Returns(PassphraseOk());
        provider.RePromptAsync(Arg.Any<BorgRepository>(), Arg.Any<CancellationToken>())
            .Returns(PassphraseOk());

        var journal = Substitute.For<IJournalStore>();
        journal.Add(Arg.Any<JournalEventKind>(), Arg.Any<object[]>(), Arg.Any<string>())
            .Returns(_ => new JournalEntry(JournalEventKind.PassphraseFailed));

        var orchestrator = new BorgOperationOrchestrator(
            Substitute.For<ILogger<BorgOperationOrchestrator>>(),
            provider,
            new TransientRetryPolicy(Substitute.For<ILogger<TransientRetryPolicy>>()),
            new BorgJobQueue(),
            journal);

        return (orchestrator, provider);
    }

    private static Task<OneOf<PassphraseResult, OperationError>> PassphraseOk() =>
        Task.FromResult<OneOf<PassphraseResult, OperationError>>(new PassphraseResult("secret"));

    private static Task<OneOf<PassphraseResult, OperationError>> PassphraseCancelled() =>
        Task.FromResult<OneOf<PassphraseResult, OperationError>>(new OperationError.Cancelled());

    private static BorgRepository CreateRepo() => new()
    {
        Name = "test",
        Path = "/test",
        EncryptionMode = BorgEncryptionMode.RepokeyBlake2
    };

    [Fact]
    public async Task PassphraseRetry_SuccessOnFirstAttempt_ReturnsSuccess()
    {
        var (orchestrator, _) = CreateOrchestrator();
        var result = await Execute(orchestrator, CreateRepo(), () => SuccessResult);
        Assert.True(result.IsT0);
    }

    [Fact]
    public async Task PassphraseRetry_NonPassphraseError_ReturnsError()
    {
        var (orchestrator, _) = CreateOrchestrator();
        var result = await Execute(orchestrator, CreateRepo(), () => GenericError);
        Assert.True(result.IsT1);
        Assert.IsType<OperationError.BorgFailure>(result.AsT1);
    }

    [Fact]
    public async Task PassphraseRetry_WrongThenSuccess_ReturnsSuccess()
    {
        var (orchestrator, _) = CreateOrchestrator();
        var attempt = 0;

        var result = await Execute(orchestrator, CreateRepo(), () =>
        {
            attempt++;
            return attempt == 1 ? WrongPassphraseResult : SuccessResult;
        });

        Assert.True(result.IsT0);
        Assert.Equal(2, attempt);
    }

    [Fact]
    public async Task PassphraseRetry_ThreeWrongAttempts_Fails()
    {
        var (orchestrator, provider) = CreateOrchestrator();
        var repo = CreateRepo();
        var attempts = 0;

        var result = await Execute(orchestrator, repo, () =>
        {
            attempts++;
            return WrongPassphraseResult;
        });

        Assert.True(result.IsT1);
        Assert.Equal(3, attempts);
        provider.Received(1).Clear(repo);
        provider.Received(1).MarkWrong(repo);
    }

    [Fact]
    public async Task PassphraseRetry_InitialPromptDenied_ReturnsCancelled()
    {
        var (orchestrator, provider) = CreateOrchestrator();
        provider.EnsureAsync(Arg.Any<BorgRepository>(), Arg.Any<CancellationToken>())
            .Returns(PassphraseCancelled());

        var repo = CreateRepo();
        var result = await Execute(orchestrator, repo, () => SuccessResult);

        Assert.True(result.IsT1);
        Assert.IsType<OperationError.Cancelled>(result.AsT1);
        provider.Received(1).MarkWrong(repo);
    }

    [Fact]
    public async Task PassphraseRetry_RePromptDenied_ReturnsCancelled()
    {
        var (orchestrator, provider) = CreateOrchestrator();
        provider.RePromptAsync(Arg.Any<BorgRepository>(), Arg.Any<CancellationToken>())
            .Returns(PassphraseCancelled());

        var repo = CreateRepo();
        var result = await Execute(orchestrator, repo, () => WrongPassphraseResult);

        Assert.True(result.IsT1);
        Assert.IsType<OperationError.Cancelled>(result.AsT1);
        provider.Received(1).MarkWrong(repo);
    }

    [Fact]
    public async Task NoPassphraseRequired_WrongPassphraseError_ReturnsErrorWithoutPrompting()
    {
        var (orchestrator, provider) = CreateOrchestrator();
        var repo = CreateRepo();
        var attempts = 0;

        var result = await Execute(orchestrator, repo, () =>
        {
            attempts++;
            return WrongPassphraseResult;
        }, requiresPassphrase: false);

        Assert.True(result.IsT1);
        Assert.IsType<OperationError.BorgFailure>(result.AsT1);
        Assert.Equal(1, attempts);
        await provider.DidNotReceive().EnsureAsync(Arg.Any<BorgRepository>(), Arg.Any<CancellationToken>());
        await provider.DidNotReceive().RePromptAsync(Arg.Any<BorgRepository>(), Arg.Any<CancellationToken>());
    }

    private static Task<OneOf<BorgExecutionResult, OperationError>> Execute(
        BorgOperationOrchestrator orchestrator,
        BorgRepository repo,
        Func<BorgResult> operation,
        bool requiresPassphrase = true) =>
        orchestrator.ExecuteAsync(
            new BorgOperationRequest(repo, NullOperationProgressSink.Instance, requiresPassphrase),
            (_, _) => Task.FromResult(operation()));
}
