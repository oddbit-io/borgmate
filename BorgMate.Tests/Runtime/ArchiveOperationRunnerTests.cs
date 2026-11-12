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

namespace BorgMate.Tests.Runtime;

public class ArchiveOperationRunnerTests
{
    [Fact]
    public async Task GetRepositoryStatsAsync_RunsStorageProbeInsideJobAndLogsMismatch()
    {
        var repository = Repository();
        var service = Substitute.For<IBorgService>();
        service.InfoRepoAsync(repository, Arg.Any<CancellationToken>())
            .Returns(new BorgResult(
                0,
                "{\"cache\":{\"stats\":{\"unique_csize\":70,\"total_chunks\":10}}}",
                ""));
        var runner = CreateRunner(service, out var storageQuery, out var logger, out var queue);
        var probeRanInsideJob = false;
        storageQuery.GetAsync(repository, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                probeRanInsideJob = BorgJob.Current.Value is not null;
                return new StorageSpaceSnapshot { CapacityBytes = 100, AvailableBytes = 40 };
            });

        var result = await runner.GetRepositoryStatsAsync(
            new RepositoryStatsOperationOptions(repository, NullOperationProgressSink.Instance));

        Assert.True(result.IsT0);
        Assert.Equal(70, result.AsT0.TotalSize);
        Assert.Equal(10, result.AsT0.TotalChunks);
        Assert.True(probeRanInsideJob);
        Assert.Equal(0, queue.PendingCount);
        var warning = Assert.Single(logger.ReceivedCalls(), IsWarningCall);
        var message = warning.GetArguments()[2]?.ToString() ?? string.Empty;
        Assert.Contains("Name=Stats repo", message);
        Assert.Contains("RepositorySizeBytes=70", message);
        Assert.Contains("UsedBytes=60", message);
        Assert.Contains("AvailableBytes=40", message);
        Assert.Contains("CapacityBytes=100", message);
    }

    [Fact]
    public async Task GetRepositoryStatsAsync_PublishesBorgDetails_WhenStorageProbeReturnsNoSnapshot()
    {
        var repository = Repository();
        var service = Substitute.For<IBorgService>();
        service.InfoRepoAsync(repository, Arg.Any<CancellationToken>())
            .Returns(new BorgResult(
                0,
                "{\"cache\":{\"stats\":{\"unique_csize\":30,\"total_chunks\":12}}}",
                ""));
        var runner = CreateRunner(service, out var storageQuery, out _, out _);
        storageQuery.GetAsync(repository, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<StorageSpaceSnapshot?>(null));

        var result = await runner.GetRepositoryStatsAsync(
            new RepositoryStatsOperationOptions(repository, NullOperationProgressSink.Instance));

        Assert.True(result.IsT0);
        Assert.Equal(30, result.AsT0.TotalSize);
        Assert.Equal(12, result.AsT0.TotalChunks);
        Assert.Null(result.AsT0.StorageSpace);
    }

    [Fact]
    public async Task GetRepositoryStatsAsync_ConfirmsRelocationAndReturnsStatistics()
    {
        var repo = Repository();
        var service = Substitute.For<IBorgService>();
        service.InfoRepoAsync(repo, Arg.Any<CancellationToken>()).Returns(
            new BorgResult((int)BorgModernExitCode.RepositoryAccessAborted, "",
                "Warning: The repository at location /repo was previously located at /old\nRepository access aborted"),
            new BorgResult(0, "{\"cache\":{\"stats\":{\"unique_csize\":30,\"total_chunks\":12}}}", ""));
        service.AcceptRelocationAsync(repo, Arg.Any<CancellationToken>()).Returns(new BorgResult(0, "", ""));
        var confirmation = new ControlledRepositoryRelocationConfirmation { Approved = true };
        var runner = CreateRunner(service, out _, out _, out _, confirmation);

        var result = await runner.GetRepositoryStatsAsync(
            new RepositoryStatsOperationOptions(repo, NullOperationProgressSink.Instance));

        Assert.True(result.IsT0);
        Assert.Equal(30, result.AsT0.TotalSize);
        Assert.Equal(1, confirmation.PromptCount);
        await service.Received(1).AcceptRelocationAsync(repo, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetRepositoryStatsAsync_NonInteractive_DoesNotConfirmNewRelocation()
    {
        var repo = Repository();
        var service = Substitute.For<IBorgService>();
        service.InfoRepoAsync(repo, Arg.Any<CancellationToken>()).Returns(
            new BorgResult((int)BorgModernExitCode.RepositoryAccessAborted, "",
                "Warning: The repository at location /repo was previously located at /old\nRepository access aborted"));
        var confirmation = new ControlledRepositoryRelocationConfirmation { Approved = true };
        var runner = CreateRunner(service, out _, out _, out _, confirmation);

        var result = await runner.GetRepositoryStatsAsync(
            new RepositoryStatsOperationOptions(repo, NullOperationProgressSink.Instance) { Interactive = false });

        Assert.True(result.IsT1);
        Assert.Equal(0, confirmation.PromptCount);
        await service.DidNotReceive().AcceptRelocationAsync(Arg.Any<BorgRepository>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetRepositoryStatsAsync_ReturnsCancelled_WhenStorageProbeIsCancelled()
    {
        var repository = Repository();
        var service = Substitute.For<IBorgService>();
        service.InfoRepoAsync(repository, Arg.Any<CancellationToken>())
            .Returns(new BorgResult(
                0,
                "{\"cache\":{\"stats\":{\"unique_csize\":30,\"total_chunks\":12}}}",
                ""));
        var runner = CreateRunner(service, out var storageQuery, out _, out _);
        storageQuery.GetAsync(repository, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<StorageSpaceSnapshot?>(new OperationCanceledException()));

        var result = await runner.GetRepositoryStatsAsync(
            new RepositoryStatsOperationOptions(repository, NullOperationProgressSink.Instance));

        Assert.True(result.IsT1);
        Assert.IsType<OperationError.Cancelled>(result.AsT1);
    }

    private static ArchiveOperationRunner CreateRunner(
        IBorgService service,
        out RepositoryStorageQuery storageQuery,
        out ILogger<ArchiveOperationRunner> logger,
        out BorgJobQueue queue,
        RepositoryRelocationConfirmation? confirmation = null)
    {
        var settings = new AppSettings();
        var wsl = new WslHelper(Substitute.For<ILogger<WslHelper>>());
        var agent = new SshAgentHelper(Substitute.For<ILogger<SshAgentHelper>>(), null, wsl);
        var transport = new SshRepositoryTransport(
            settings, agent, wsl, Substitute.For<WslSshHostKeyTrust>(Substitute.For<ILogger<WslSshHostKeyTrust>>()),
            Substitute.For<ILogger<SshRepositoryTransport>>());
        var mounter = Substitute.For<INetworkShareMounter>();
        var factory = Substitute.For<BorgServiceFactory>(
            Substitute.For<ILoggerFactory>(), settings, transport, wsl, mounter, null);
        factory.GetService(Arg.Any<BorgVersion>()).Returns(service);
        storageQuery = Substitute.For<RepositoryStorageQuery>(
            mounter, transport, Substitute.For<ILogger<RepositoryStorageQuery>>());
        queue = new BorgJobQueue();
        var journal = Substitute.For<IJournalStore>();
        var passphrase = new PromptingPassphraseProvider(null);
        var orchestrator = new BorgOperationOrchestrator(
            Substitute.For<ILogger<BorgOperationOrchestrator>>(),
            passphrase,
            factory,
            new TransientRetryPolicy(Substitute.For<ILogger<TransientRetryPolicy>>()),
            queue,
            journal,
            confirmation ?? new ControlledRepositoryRelocationConfirmation());
        logger = Substitute.For<ILogger<ArchiveOperationRunner>>();
        return new ArchiveOperationRunner(
            factory,
            queue,
            new BorgCacheService(),
            orchestrator,
            storageQuery,
            logger);
    }

    private static BorgRepository Repository()
    {
        return new BorgRepository
        {
            Name = "Stats repo",
            Path = "/repo",
            EncryptionMode = BorgEncryptionMode.None
        };
    }

    private static bool IsWarningCall(NSubstitute.Core.ICall call)
    {
        return call.GetMethodInfo().Name == nameof(ILogger.Log) &&
               call.GetArguments().FirstOrDefault() is LogLevel.Warning;
    }
}
