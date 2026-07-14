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
using JournalEntry = BorgMate.Domain.Journal.JournalEntry;
using WslHelper = BorgMate.Infrastructure.Platform.WslHelper;

namespace BorgMate.Tests.Runtime;

public class RepositoryVerificationOperationRunnerTests
{
    private static readonly WslHelper Wsl = new(Substitute.For<ILogger<WslHelper>>());
    private static readonly SshAgentHelper SshAgent = new(Substitute.For<ILogger<SshAgentHelper>>(), null, Wsl);
    private static readonly INetworkShareMounter Mounter = Substitute.For<INetworkShareMounter>();
    private static readonly AppSettings Settings = new();
    private static readonly SshRepositoryTransport SharedSshTransport = new(
        Settings, SshAgent, Wsl, Substitute.For<ILogger<SshRepositoryTransport>>());

    [Fact]
    public async Task ValidateConfiguredBorgAsync_AcceptsBorg14ForBorg1Repository()
    {
        var service = new VersionBorg1Service("borg 1.4.4");

        var error = await RepositoryVerificationOperationRunner.ValidateConfiguredBorgAsync(service, CancellationToken.None);

        Assert.Null(error);
    }

    [Fact]
    public async Task ValidateConfiguredBorgAsync_AcceptsBorg20Beta21ForBorg2Repository()
    {
        var service = new VersionBorg2Service("borg 2.0.0b21");

        var error = await RepositoryVerificationOperationRunner.ValidateConfiguredBorgAsync(service, CancellationToken.None);

        Assert.Null(error);
    }

    [Fact]
    public async Task ValidateConfiguredBorgAsync_RejectsUnknownVersionText()
    {
        var service = new VersionBorg1Service("not borg");

        var error = await RepositoryVerificationOperationRunner.ValidateConfiguredBorgAsync(service, CancellationToken.None);

        var validation = Assert.IsType<OperationError.Validation>(error);
        Assert.Equal("Validation.BorgVersionUnknown", validation.MessageKey);
    }

    [Fact]
    public async Task ValidateConfiguredBorgAsync_RejectsWrongBinaryGeneration()
    {
        var service = new VersionBorg2Service("borg 1.4.4");

        var error = await RepositoryVerificationOperationRunner.ValidateConfiguredBorgAsync(service, CancellationToken.None);

        var validation = Assert.IsType<OperationError.Validation>(error);
        Assert.Equal("Validation.BorgVersionMismatch", validation.MessageKey);
    }

    [Fact]
    public async Task ValidateConfiguredBorgAsync_RejectsOlderBorg2Beta()
    {
        var service = new VersionBorg2Service("borg 2.0.0b20");

        var error = await RepositoryVerificationOperationRunner.ValidateConfiguredBorgAsync(service, CancellationToken.None);

        var validation = Assert.IsType<OperationError.Validation>(error);
        Assert.Equal("Validation.Borg2TooOld", validation.MessageKey);
    }

    [Fact]
    public async Task ValidateConfiguredBorgAsync_StoresDetectedBorg2VersionOnService()
    {
        var service = new VersionBorg2Service("borg 2.0.0b21");

        var error = await RepositoryVerificationOperationRunner.ValidateConfiguredBorgAsync(service, CancellationToken.None);

        Assert.Null(error);
        Assert.NotNull(service.DetectedVersion);
        Assert.Equal(21, service.DetectedVersion.Beta);
    }

    [Fact]
    public async Task VerifyExistingRepositoryAsync_UnencryptedRepo_SucceedsWithoutPromptAndDetectsNone()
    {
        // Candidate carries the editor default (encrypted) — the bug scenario from issue #1.
        var repo = new BorgRepository
        {
            Name = "plain",
            Path = "/plain",
            EncryptionMode = BorgEncryptionMode.RepokeyBlake2
        };
        var provider = new CountingPromptProvider("unused");
        var service = new ScriptedInfoBorg1Service(provider,
            new BorgResult(0, """{"encryption": {"mode": "none"}}""", ""));
        var runner = CreateRunner(service, provider);

        var result = await runner.VerifyExistingRepositoryAsync(
            new RepositoryVerificationOperationOptions(repo, NullOperationProgressSink.Instance, false));

        Assert.True(result.IsT0);
        Assert.Equal(0, provider.PromptCount);
        Assert.Equal(BorgEncryptionMode.None, repo.EncryptionMode);
    }

    [Fact]
    public async Task VerifyExistingRepositoryAsync_EncryptedRepo_PromptsOnceAndDetectsMode()
    {
        var repo = new BorgRepository
        {
            Name = "enc",
            Path = "/enc",
            EncryptionMode = BorgEncryptionMode.RepokeyBlake2
        };
        var provider = new CountingPromptProvider("secret");
        var service = new ScriptedInfoBorg1Service(provider,
            new BorgResult((int)BorgModernExitCode.NoPassphraseFailure, "", "passphrase required"),
            new BorgResult(0, """{"encryption": {"mode": "repokey"}}""", ""));
        var runner = CreateRunner(service, provider);

        var result = await runner.VerifyExistingRepositoryAsync(
            new RepositoryVerificationOperationOptions(repo, NullOperationProgressSink.Instance, false));

        Assert.True(result.IsT0);
        Assert.Equal(1, provider.PromptCount);
        Assert.Equal(BorgEncryptionMode.Repokey, repo.EncryptionMode);
    }

    [Fact]
    public async Task CheckRemotePathAsync_UsesConfiguredPortAndSafelyQuotedPath()
    {
        var transport = Substitute.For<SshRepositoryTransport>(
            Settings, SshAgent, Wsl, Substitute.For<ILogger<SshRepositoryTransport>>());
        transport.RunAsync(
                Arg.Any<BorgRepository>(),
                Arg.Any<SshRepositoryTarget>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(new BorgResult(0, "/opt/borg\n", ""));
        var provider = new CountingPromptProvider("unused");
        var runner = CreateRunner(Substitute.For<IBorgService>(), provider, transport);

        var result = await runner.CheckRemotePathAsync(
            new RemotePathCheckOperationOptions(
                new BorgRepository { BorgVersion = BorgVersion.Borg2 },
                NullOperationProgressSink.Instance,
                "backup@example.com",
                "/opt/borg dir/it's;safe",
                "/keys/backup",
                2202));

        Assert.True(result.IsT0);
        await transport.Received(1).RunAsync(
            Arg.Is<BorgRepository>(repository =>
                repository.SshPort == 2202 && repository.SshKeyPath == "/keys/backup"),
            Arg.Is<SshRepositoryTarget>(target =>
                target.Host == "backup@example.com" && target.Port == 2202),
            "command -v '/opt/borg dir/it'\"'\"'s;safe'",
            Arg.Any<CancellationToken>());
    }

    private static RepositoryVerificationOperationRunner CreateRunner(
        IBorgService service,
        PromptingPassphraseProvider provider,
        SshRepositoryTransport? sshTransport = null)
    {
        sshTransport ??= SharedSshTransport;
        var queue = new BorgJobQueue();
        var journal = Substitute.For<IJournalStore>();
        journal.Add(Arg.Any<JournalEventKind>(), Arg.Any<object[]>(), Arg.Any<string>())
            .Returns(_ => new JournalEntry(JournalEventKind.PassphraseFailed));
        var orchestrator = new BorgOperationOrchestrator(
            Substitute.For<ILogger<BorgOperationOrchestrator>>(),
            provider,
            new TransientRetryPolicy(Substitute.For<ILogger<TransientRetryPolicy>>()),
            queue,
            journal);
        return new RepositoryVerificationOperationRunner(
            new StubBorgServiceFactory(service, sshTransport), queue, journal, orchestrator, provider, sshTransport);
    }

    private sealed class StubBorgServiceFactory : BorgServiceFactory
    {
        private readonly IBorgService service;

        public StubBorgServiceFactory(IBorgService service, SshRepositoryTransport sshTransport)
            : base(Substitute.For<ILoggerFactory>(), Settings, sshTransport, Wsl, Mounter)
        {
            this.service = service;
        }

        public override IBorgService GetService(BorgVersion borgVersion) => service;
    }

    /// <summary>Answers "--version" with borg 1.4.4 and "info" commands with the scripted results in order.</summary>
    private sealed class ScriptedInfoBorg1Service(
        PromptingPassphraseProvider provider, params BorgResult[] infoResults)
        : Borg1Service(
            Substitute.For<ILogger<Borg1Service>>(),
            Settings,
            SharedSshTransport,
            Wsl,
            Mounter,
            provider)
    {
        private readonly Queue<BorgResult> _infoResults = new(infoResults);

        protected override Task<BorgResult> RunCommandAsync(
            string fileName,
            string arguments,
            BorgEnv? env = null,
            string? workingDirectory = null,
            CancellationToken ct = default,
            Action<string>? onStderrLine = null)
        {
            return Task.FromResult(arguments == "--version"
                ? new BorgResult(0, "borg 1.4.4", "")
                : _infoResults.Dequeue());
        }
    }

    private sealed class CountingPromptProvider(string promptedPassphrase) : PromptingPassphraseProvider(null)
    {
        public int PromptCount { get; private set; }

        public override Task<bool> EnsurePassphraseAsync(BorgRepository repo)
        {
            if (!repo.IsEncrypted || !string.IsNullOrWhiteSpace(GetCachedPassphrase(repo)))
                return Task.FromResult(true);

            PromptCount++;
            Set(repo, promptedPassphrase);
            return Task.FromResult(true);
        }

        public override Task<bool> RePromptAsync(BorgRepository repo)
        {
            PromptCount++;
            Set(repo, promptedPassphrase);
            return Task.FromResult(true);
        }
    }

    private sealed class VersionBorg1Service(string versionText)
        : Borg1Service(
            Substitute.For<ILogger<Borg1Service>>(),
            Settings,
            SharedSshTransport,
            Wsl,
            Mounter,
            new PromptingPassphraseProvider(null))
    {
        protected override Task<BorgResult> RunCommandAsync(
            string fileName,
            string arguments,
            BorgEnv? env = null,
            string? workingDirectory = null,
            CancellationToken ct = default,
            Action<string>? onStderrLine = null)
        {
            return Task.FromResult(new BorgResult(0, versionText, ""));
        }
    }

    private sealed class VersionBorg2Service(string versionText)
        : Borg2Service(
            Substitute.For<ILogger<Borg2Service>>(),
            Settings,
            SharedSshTransport,
            Wsl,
            Mounter,
            new PromptingPassphraseProvider(null))
    {
        protected override Task<BorgResult> RunCommandAsync(
            string fileName,
            string arguments,
            BorgEnv? env = null,
            string? workingDirectory = null,
            CancellationToken ct = default,
            Action<string>? onStderrLine = null)
        {
            return Task.FromResult(new BorgResult(0, versionText, ""));
        }
    }
}
