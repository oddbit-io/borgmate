using BorgMate.Domain.Repositories;
using BorgMate.Domain.Settings;
using BorgMate.Infrastructure.Platform;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace BorgMate.Tests.Infrastructure;

public class SshRepositoryTransportTests
{
    [Theory]
    [InlineData("backup@example.com:repo", BorgRepositoryLocationKind.SshFields)]
    [InlineData("ssh://backup@example.com/repo", BorgRepositoryLocationKind.BorgUrl)]
    [InlineData("sftp://backup@example.com/repo", BorgRepositoryLocationKind.BorgUrl)]
    public void UsesSsh_RecognizesStructuredAndUrlLocations(
        string location,
        BorgRepositoryLocationKind locationKind)
    {
        var repository = new BorgRepository
        {
            Path = location,
            BorgUrl = locationKind == BorgRepositoryLocationKind.BorgUrl ? location : string.Empty,
            LocationKind = locationKind,
            IsLocal = false
        };

        Assert.True(CreateTransport().UsesSsh(repository));
    }

    [Fact]
    public void ResolveTarget_UsesConfiguredPort_ForLegacyLocation()
    {
        var transport = CreateTransport();
        var repository = new BorgRepository
        {
            Path = "backup@example.com:repo/path",
            LocationKind = BorgRepositoryLocationKind.SshFields,
            IsLocal = false,
            SshPort = 2202
        };

        var target = transport.ResolveTarget(repository);

        Assert.Equal("backup@example.com", target.Host);
        Assert.Equal("repo/path", target.RepositoryPath);
        Assert.Equal(2202, target.Port);
    }

    [Fact]
    public void ResolveTarget_UsesUrlPortAndStripsRelativePathMarker()
    {
        var transport = CreateTransport();
        var repository = new BorgRepository
        {
            Path = "ssh://backup@example.com:2222/./repo/path",
            BorgUrl = "ssh://backup@example.com:2222/./repo/path",
            LocationKind = BorgRepositoryLocationKind.BorgUrl,
            BorgVersion = BorgVersion.Borg2,
            SshPort = 2202
        };

        var target = transport.ResolveTarget(repository);

        Assert.Equal("backup@example.com", target.Host);
        Assert.Equal("repo/path", target.RepositoryPath);
        Assert.Equal(2222, target.Port);
    }

    [Fact]
    public async Task PrepareForBorgAsync_UsesCopiedWslKeyAndAskPass()
    {
        var wsl = Substitute.For<WslHelper>(Substitute.For<ILogger<WslHelper>>());
        wsl.RequiresWsl.Returns(true);
        wsl.GetWslKeyPathAsync("C:\\keys\\backup key", Arg.Any<CancellationToken>())
            .Returns("/home/user/.ssh/backup key");
        wsl.CopySshKeyAsync("C:\\keys\\backup key", Arg.Any<CancellationToken>())
            .Returns("/home/user/.ssh/backup key");
        var agent = Substitute.For<SshAgentHelper>(
            Substitute.For<ILogger<SshAgentHelper>>(), null, wsl, null);
        agent.EnsureKeyLoadedAsync(Arg.Any<string>(), Arg.Any<BorgRepository>(), Arg.Any<CancellationToken>())
            .Returns(true);
        agent.GetWslKeyPassphrase("C:\\keys\\backup key").Returns("it's secret");
        var transport = new SshRepositoryTransport(
            new AppSettings(), agent, wsl, Substitute.For<ILogger<SshRepositoryTransport>>());
        var repository = new BorgRepository
        {
            Path = "backup@example.com:repo",
            LocationKind = BorgRepositoryLocationKind.SshFields,
            IsLocal = false,
            SshKeyPath = "C:\\keys\\backup key",
            SshPort = 2202
        };

        await transport.PrepareKeyAsync(repository, repository.SshKeyPath);
        var prepared = await transport.PrepareForBorgAsync(repository);

        await wsl.Received(1).CopySshKeyAsync(repository.SshKeyPath, Arg.Any<CancellationToken>());
        Assert.Contains("/home/user/.ssh/backup key", prepared.Arguments);
        Assert.Contains("2202", prepared.Arguments);
        Assert.Contains("PasswordAuthentication=no", prepared.Arguments);
        Assert.Contains("KbdInteractiveAuthentication=no", prepared.Arguments);
        Assert.DoesNotContain("BatchMode=yes", prepared.Arguments);
        var preCommands = Assert.IsType<List<string>>(prepared.WslPreCommands);
        Assert.Contains(preCommands, command => command.Contains("SSH_ASKPASS_REQUIRE=force"));
        Assert.Contains(preCommands, command => command.Contains("it'\\''s secret"));
    }

    [Fact]
    public void BuildCommandArguments_AllowsAskPassOnWsl()
    {
        var arguments = SshRepositoryTransport.BuildCommandArguments(
            new PreparedSshConnection { Arguments = [], BorgRsh = "ssh" },
            new SshRepositoryTarget { Host = "backup@example.com", RepositoryPath = "repo" },
            "df -P -k 'repo'",
            true);

        Assert.Contains("PasswordAuthentication=no", arguments);
        Assert.Contains("KbdInteractiveAuthentication=no", arguments);
        Assert.DoesNotContain("BatchMode=yes", arguments);
        Assert.Equal("df -P -k 'repo'", arguments[^1]);
    }

    [Fact]
    public void QuoteRemoteArgument_QuotesSpacesQuotesAndShellMetacharacters()
    {
        var quoted = SshRepositoryTransport.QuoteRemoteArgument("repo dir/it's;$(unsafe)");

        Assert.Equal("'repo dir/it'\"'\"'s;$(unsafe)'", quoted);
    }

    [Fact]
    public void BuildWslCommandArguments_PassesShellCommandAsOneArgument()
    {
        var arguments = SshRepositoryTransport.BuildWslCommandArguments(
            ["--", "backup@example.com", "df -P -k 'repo dir;$(unsafe)'"],
            ["export SSH_ASKPASS=/tmp/askpass"]);

        Assert.Equal(["--", "bash", "-c"], arguments.Take(3));
        Assert.Contains("export SSH_ASKPASS=/tmp/askpass", arguments[3]);
        Assert.Contains("$'df -P -k \\'repo dir;$(unsafe)\\''", arguments[3]);
    }

    private static SshRepositoryTransport CreateTransport()
    {
        var wsl = new WslHelper(Substitute.For<ILogger<WslHelper>>());
        var agent = new SshAgentHelper(Substitute.For<ILogger<SshAgentHelper>>(), null, wsl);
        return new SshRepositoryTransport(
            new AppSettings(), agent, wsl, Substitute.For<ILogger<SshRepositoryTransport>>());
    }
}
