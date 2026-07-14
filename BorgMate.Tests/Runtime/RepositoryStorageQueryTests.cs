using System.IO;
using BorgMate.Domain.Repositories;
using BorgMate.Domain.Settings;
using BorgMate.Infrastructure.Borg;
using BorgMate.Infrastructure.Platform;
using BorgMate.Runtime.Operations;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace BorgMate.Tests.Runtime;

public class RepositoryStorageQueryTests
{
    [Fact]
    public async Task GetAsync_QueriesLocalPath()
    {
        var query = CreateQuery(out _, out _);
        var repository = new BorgRepository
        {
            Path = Path.GetTempPath(),
            LocationKind = BorgRepositoryLocationKind.LocalPath
        };

        var snapshot = await query.GetAsync(repository);

        Assert.NotNull(snapshot);
        Assert.True(snapshot.IsValid);
    }

    [Fact]
    public async Task GetAsync_QueriesFileUrl()
    {
        var query = CreateQuery(out _, out _);
        var url = new Uri(Path.GetTempPath()).AbsoluteUri;
        var repository = new BorgRepository
        {
            Path = url,
            BorgUrl = url,
            LocationKind = BorgRepositoryLocationKind.BorgUrl,
            BorgVersion = BorgVersion.Borg2
        };

        var snapshot = await query.GetAsync(repository);

        Assert.NotNull(snapshot);
        Assert.True(snapshot.IsValid);
    }

    [Fact]
    public async Task GetAsync_QueriesMountedSmbShare()
    {
        var query = CreateQuery(out var mounter, out _);
        var expected = Snapshot();
        mounter.GetStorageSpaceAsync(Arg.Any<SmbLocation>(), Arg.Any<CancellationToken>())
            .Returns(expected);
        var repository = new BorgRepository
        {
            Path = "smb://nas/backups/repo",
            LocationKind = BorgRepositoryLocationKind.SmbShare,
            IsLocal = false
        };

        var snapshot = await query.GetAsync(repository);

        Assert.Same(expected, snapshot);
        await mounter.Received(1).GetStorageSpaceAsync(
            Arg.Is<SmbLocation>(location => location.Host == "nas" && location.Share == "backups"),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("backup@example.com:repo/path", BorgRepositoryLocationKind.SshFields)]
    [InlineData("ssh://backup@example.com/repo/path", BorgRepositoryLocationKind.BorgUrl)]
    [InlineData("sftp://backup@example.com/repo/path", BorgRepositoryLocationKind.BorgUrl)]
    public async Task GetAsync_QueriesSshTransport(string location, BorgRepositoryLocationKind locationKind)
    {
        var query = CreateQuery(out _, out var transport);
        var repository = new BorgRepository
        {
            Path = location,
            BorgUrl = locationKind == BorgRepositoryLocationKind.BorgUrl ? location : string.Empty,
            LocationKind = locationKind,
            IsLocal = false,
            BorgVersion = BorgVersion.Borg2
        };
        var target = new SshRepositoryTarget
        {
            Host = "backup@example.com",
            RepositoryPath = "repo dir/it's;safe",
            Port = 22
        };
        transport.UsesSsh(repository).Returns(true);
        transport.ResolveTarget(repository).Returns(target);
        transport.RunAsync(repository, target, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new BorgResult(
                0,
                "Filesystem 1024-blocks Used Available Capacity Mounted on\n/dev/sda 100 60 40 60% /repo\n",
                ""));

        var snapshot = await query.GetAsync(repository);

        Assert.NotNull(snapshot);
        Assert.Equal(100 * 1024, snapshot.CapacityBytes);
        await transport.Received(1).RunAsync(
            repository,
            target,
            "df -P -k 'repo dir/it'\"'\"'s;safe'",
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("rest://example.com/repo")]
    [InlineData("s3://bucket/repo")]
    [InlineData("b2://bucket/repo")]
    [InlineData("rclone:remote:path")]
    public async Task GetAsync_ReturnsNullWithoutWarning_ForUnsupportedTransport(string location)
    {
        var query = CreateQuery(out _, out var transport, out var logger);
        var repository = new BorgRepository
        {
            Path = location,
            BorgUrl = location,
            LocationKind = BorgRepositoryLocationKind.BorgUrl,
            BorgVersion = BorgVersion.Borg2
        };
        transport.UsesSsh(repository).Returns(false);

        var snapshot = await query.GetAsync(repository);

        Assert.Null(snapshot);
        Assert.DoesNotContain(logger.ReceivedCalls(), IsWarningCall);
    }

    [Fact]
    public async Task GetAsync_LogsWarningAndReturnsNull_WhenSshProbeFails()
    {
        var query = CreateQuery(out _, out var transport, out var logger);
        var repository = new BorgRepository
        {
            Name = "Remote repo",
            Path = "backup@example.com:repo",
            LocationKind = BorgRepositoryLocationKind.SshFields,
            IsLocal = false
        };
        var target = new SshRepositoryTarget { Host = "backup@example.com", RepositoryPath = "repo" };
        transport.UsesSsh(repository).Returns(true);
        transport.ResolveTarget(repository).Returns(target);
        transport.RunAsync(repository, target, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new BorgResult(255, "", "connection failed"));

        var snapshot = await query.GetAsync(repository);

        Assert.Null(snapshot);
        var warning = Assert.Single(logger.ReceivedCalls(), IsWarningCall);
        var message = warning.GetArguments()[2]?.ToString() ?? string.Empty;
        Assert.Contains("Location=***@example.com:repo", message);
        Assert.Contains("ExitCode=255", message);
    }

    [Fact]
    public async Task GetAsync_LogsExceptionType_WhenLocalProbeFails()
    {
        var query = CreateQuery(out _, out _, out var logger);
        var repository = new BorgRepository
        {
            Name = "Missing repo",
            Path = "/nonexistent/borgmate/storage-query/path",
            LocationKind = BorgRepositoryLocationKind.LocalPath
        };

        var snapshot = await query.GetAsync(repository);

        Assert.Null(snapshot);
        var warning = Assert.Single(logger.ReceivedCalls(), IsWarningCall);
        var message = warning.GetArguments()[2]?.ToString() ?? string.Empty;
        Assert.Contains("ExceptionType=DirectoryNotFoundException", message);
    }

    [Fact]
    public async Task GetAsync_PropagatesCancellation()
    {
        var query = CreateQuery(out _, out var transport);
        var repository = new BorgRepository
        {
            Path = "backup@example.com:repo",
            LocationKind = BorgRepositoryLocationKind.SshFields,
            IsLocal = false
        };
        var target = new SshRepositoryTarget { Host = "backup@example.com", RepositoryPath = "repo" };
        transport.UsesSsh(repository).Returns(true);
        transport.ResolveTarget(repository).Returns(target);
        transport.RunAsync(repository, target, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new BorgResult(-1, "", "", true));

        await Assert.ThrowsAsync<OperationCanceledException>(() => query.GetAsync(repository));
    }

    private static RepositoryStorageQuery CreateQuery(
        out INetworkShareMounter mounter,
        out SshRepositoryTransport transport)
    {
        return CreateQuery(out mounter, out transport, out _);
    }

    private static RepositoryStorageQuery CreateQuery(
        out INetworkShareMounter mounter,
        out SshRepositoryTransport transport,
        out ILogger<RepositoryStorageQuery> logger)
    {
        mounter = Substitute.For<INetworkShareMounter>();
        var wsl = new WslHelper(Substitute.For<ILogger<WslHelper>>());
        var agent = new SshAgentHelper(Substitute.For<ILogger<SshAgentHelper>>(), null, wsl);
        transport = Substitute.For<SshRepositoryTransport>(
            new AppSettings(), agent, wsl, Substitute.For<ILogger<SshRepositoryTransport>>());
        logger = Substitute.For<ILogger<RepositoryStorageQuery>>();
        return new RepositoryStorageQuery(mounter, transport, logger);
    }

    private static StorageSpaceSnapshot Snapshot()
    {
        return new StorageSpaceSnapshot { CapacityBytes = 100, AvailableBytes = 40 };
    }

    private static bool IsWarningCall(NSubstitute.Core.ICall call)
    {
        return call.GetMethodInfo().Name == nameof(ILogger.Log) &&
               call.GetArguments().FirstOrDefault() is LogLevel.Warning;
    }
}
