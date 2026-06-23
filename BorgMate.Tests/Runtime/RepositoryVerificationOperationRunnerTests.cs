using BorgMate.Domain.Repositories;
using BorgMate.Domain.Settings;
using BorgMate.Infrastructure.Borg;
using BorgMate.Infrastructure.Platform;
using BorgMate.Runtime.Operations;
using BorgMate.Runtime.Orchestration;
using BorgMate.Runtime.Security;
using Microsoft.Extensions.Logging;
using NSubstitute;
using WslHelper = BorgMate.Infrastructure.Platform.WslHelper;

namespace BorgMate.Tests.Runtime;

public class RepositoryVerificationOperationRunnerTests
{
    private static readonly WslHelper Wsl = new(Substitute.For<ILogger<WslHelper>>());
    private static readonly SshAgentHelper SshAgent = new(Substitute.For<ILogger<SshAgentHelper>>(), null, Wsl);

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

    private sealed class VersionBorg1Service(string versionText)
        : Borg1Service(
            Substitute.For<ILogger<Borg1Service>>(),
            new AppSettings(),
            SshAgent,
            Wsl,
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
            new AppSettings(),
            SshAgent,
            Wsl,
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
