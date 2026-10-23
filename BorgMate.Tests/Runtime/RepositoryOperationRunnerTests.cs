using BorgMate.Domain.Journal;
using BorgMate.Domain.Repositories;
using BorgMate.Domain.Settings;
using BorgMate.Infrastructure.Borg;
using BorgMate.Infrastructure.Platform;
using BorgMate.Persistence.Configuration;
using BorgMate.Persistence.Journal;
using BorgMate.Persistence.Keychain;
using BorgMate.Runtime.Jobs;
using BorgMate.Runtime.Operations;
using BorgMate.Runtime.Orchestration;
using BorgMate.Runtime.Progress;
using BorgMate.Runtime.Security;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace BorgMate.Tests.Runtime;

public class RepositoryOperationRunnerTests
{
    [Fact]
    public async Task BackupAsync_CompletesWithWarnings_WhenBorgCannotReadSourceFile()
    {
        const string stderr = "/Users/user/.Trash: dir_open: [Errno 1] Operation not permitted: '.Trash'";
        var repository = Repository();
        var service = Substitute.For<IBorgService>();
        service.CreateBackupAsync(repository, Arg.Any<string>(), Arg.Any<IEnumerable<string>>(),
                Arg.Any<CancellationToken>(), Arg.Any<Action<string>?>())
            .Returns(new BorgResult((int)BorgModernExitCode.BackupPermissionError, "", stderr));
        var journal = Substitute.For<IJournalStore>();
        var entry = new JournalEntry(JournalEventKind.Backup, [repository.Name], repositoryName: repository.Name);
        journal.Add(JournalEventKind.Backup, Arg.Any<object[]>(), repository.Name).Returns(entry);
        var runner = CreateRunner(service, journal);

        var result = await runner.BackupAsync(
            new BackupOperationOptions(repository, NullOperationProgressSink.Instance));

        Assert.True(result.IsT0);
        Assert.NotNull(repository.LastBackupAt);
        journal.Received(1).Complete(
            entry,
            JournalResult.CompletedWithWarnings,
            Arg.Is<string>(detail => detail.Contains("permission to read a file") && detail.Contains(stderr)));
    }

    private static RepositoryOperationRunner CreateRunner(IBorgService service, IJournalStore journal)
    {
        var settings = new AppSettings();
        var wsl = new WslHelper(Substitute.For<ILogger<WslHelper>>());
        var agent = new SshAgentHelper(Substitute.For<ILogger<SshAgentHelper>>(), null, wsl);
        var transport = new SshRepositoryTransport(
            settings, agent, wsl, Substitute.For<ILogger<SshRepositoryTransport>>());
        var factory = Substitute.For<BorgServiceFactory>(
            Substitute.For<ILoggerFactory>(), settings, transport, wsl, Substitute.For<INetworkShareMounter>(), null);
        factory.GetService(Arg.Any<BorgVersion>()).Returns(service);
        var queue = new BorgJobQueue();
        var passphrase = new PromptingPassphraseProvider(null);
        var orchestrator = new BorgOperationOrchestrator(
            Substitute.For<ILogger<BorgOperationOrchestrator>>(),
            passphrase,
            new TransientRetryPolicy(Substitute.For<ILogger<TransientRetryPolicy>>()),
            queue,
            journal);
        return new RepositoryOperationRunner(
            factory,
            queue,
            journal,
            orchestrator,
            new DirectorySizeCalculator(Substitute.For<ILogger<DirectorySizeCalculator>>()),
            new BorgCacheService(),
            Substitute.For<IRepositoryConfigStore>(),
            Substitute.For<ISettingsStore>(),
            Substitute.For<ICredentialStore>(),
            passphrase);
    }

    private static BorgRepository Repository()
    {
        return new BorgRepository
        {
            Name = "MacBook",
            Path = "/repo",
            EncryptionMode = BorgEncryptionMode.None
        };
    }
}
