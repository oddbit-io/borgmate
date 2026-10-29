using BorgMate.Domain.Journal;
using BorgMate.Domain.Repositories;
using BorgMate.Domain.Settings;
using BorgMate.Infrastructure.Borg;
using BorgMate.Infrastructure.Platform;
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
using WslHelper = BorgMate.Infrastructure.Platform.WslHelper;

namespace BorgMate.Tests.Runtime;

public class BorgOperationOrchestratorTests
{
    private static readonly BorgResult SuccessResult = new(0, "ok", "");
    private static readonly BorgResult WrongPassphraseResult = new(
        (int)BorgModernExitCode.PassphraseWrong,
        "",
        "wrong passphrase");
    private static readonly BorgResult GenericError = new((int)BorgModernExitCode.Error, "", "some error");

    private static readonly RepositoryRelocation Relocation = new()
    {
        Location = "/mnt/d/backup",
        PreviousLocation = "/mnt/e/backup"
    };

    private static readonly BorgResult RelocationAbortedResult = new(
        (int)BorgModernExitCode.RepositoryAccessAborted,
        "",
        $"Warning: The repository at location {Relocation.Location} was previously located at {Relocation.PreviousLocation}\n" +
        "Do you want to continue? [yN] no (from BORG_RELOCATED_REPO_ACCESS_IS_OK)\n" +
        "Aborting.\n" +
        "Repository access aborted\n");

    private static (BorgOperationOrchestrator orchestrator, IPassphraseProvider provider) CreateOrchestrator(
        IBorgService? service = null)
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
            CreateServiceFactory(service ?? CreateRelocationAcceptingService(SuccessResult)),
            new TransientRetryPolicy(Substitute.For<ILogger<TransientRetryPolicy>>()),
            new BorgJobQueue(),
            journal,
            new ControlledRepositoryRelocationConfirmation());

        return (orchestrator, provider);
    }

    private static BorgServiceFactory CreateServiceFactory(IBorgService service)
    {
        var settings = new AppSettings();
        var wsl = new WslHelper(Substitute.For<ILogger<WslHelper>>());
        var agent = new SshAgentHelper(Substitute.For<ILogger<SshAgentHelper>>(), null, wsl);
        var transport = new SshRepositoryTransport(
            settings, agent, wsl, Substitute.For<ILogger<SshRepositoryTransport>>());
        var factory = Substitute.For<BorgServiceFactory>(
            Substitute.For<ILoggerFactory>(), settings, transport, wsl, Substitute.For<INetworkShareMounter>(), null);
        factory.GetService(Arg.Any<BorgVersion>()).Returns(service);
        return factory;
    }

    private static IBorgService CreateRelocationAcceptingService(BorgResult acceptResult)
    {
        var service = Substitute.For<IBorgService>();
        service.AcceptRelocationAsync(Arg.Any<BorgRepository>(), Arg.Any<CancellationToken>())
            .Returns(acceptResult);
        return service;
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

    [Fact]
    public async Task Relocation_ConfirmedPair_AcceptsAndRepeats()
    {
        var service = CreateRelocationAcceptingService(SuccessResult);
        var (orchestrator, _) = CreateOrchestrator(service);
        var repo = CreateRepo();
        repo.ConfirmedRelocations = [Relocation];
        var runs = 0;

        var result = await Execute(orchestrator, repo, () => ++runs == 1 ? RelocationAbortedResult : SuccessResult);

        Assert.True(result.IsT0);
        Assert.Equal(2, runs);
        await service.Received(1).AcceptRelocationAsync(repo, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Relocation_UnconfirmedPair_ReturnsBorgRefusalWithoutAccepting()
    {
        var service = CreateRelocationAcceptingService(SuccessResult);
        var (orchestrator, _) = CreateOrchestrator(service);
        var runs = 0;

        var result = await Execute(orchestrator, CreateRepo(), () =>
        {
            runs++;
            return RelocationAbortedResult;
        });

        Assert.True(result.IsT1);
        Assert.Equal(Relocation, result.AsT1.ReportedRelocation());
        Assert.Equal(1, runs);
        await service.DidNotReceive().AcceptRelocationAsync(Arg.Any<BorgRepository>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Relocation_AcceptCommandFails_ReturnsItsErrorWithoutRepeating()
    {
        var (orchestrator, _) = CreateOrchestrator(CreateRelocationAcceptingService(GenericError));
        var repo = CreateRepo();
        repo.ConfirmedRelocations = [Relocation];
        var runs = 0;

        var result = await Execute(orchestrator, repo, () =>
        {
            runs++;
            return RelocationAbortedResult;
        });

        Assert.True(result.IsT1);
        var failure = Assert.IsType<OperationError.BorgFailure>(result.AsT1);
        Assert.Equal(BorgModernExitCode.Error, failure.Code);
        Assert.Equal(1, runs);
    }

    [Fact]
    public async Task Relocation_AbortWithoutRelocationWarning_ReportsNoRelocation()
    {
        var (orchestrator, _) = CreateOrchestrator();

        var result = await Execute(orchestrator, CreateRepo(),
            () => new BorgResult((int)BorgModernExitCode.RepositoryAccessAborted, "", "Repository access aborted"));

        Assert.True(result.IsT1);
        Assert.Null(result.AsT1.ReportedRelocation());
    }

    [Fact]
    public async Task Relocation_AfterWrongPassphrases_DoesNotUseAPassphraseAttempt()
    {
        var (orchestrator, _) = CreateOrchestrator();
        var repo = CreateRepo();
        repo.ConfirmedRelocations = [Relocation];
        var runs = 0;

        var result = await Execute(orchestrator, repo, () => ++runs switch
        {
            1 or 2 => WrongPassphraseResult,
            3 => RelocationAbortedResult,
            _ => SuccessResult
        });

        Assert.True(result.IsT0);
        Assert.Equal(4, runs);
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
