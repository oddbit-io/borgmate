using BorgMate.Domain.Repositories;
using BorgMate.Domain.Settings;
using BorgMate.Infrastructure.Borg;
using BorgMate.Infrastructure.Platform;
using Microsoft.Extensions.Logging;
using NSubstitute;
using WslHelper = BorgMate.Infrastructure.Platform.WslHelper;

namespace BorgMate.Tests.Infrastructure;

public class BorgServiceFactoryTests
{
    private static readonly WslHelper Wsl = new(Substitute.For<ILogger<WslHelper>>());
    private readonly BorgServiceFactory _factory;

    public BorgServiceFactoryTests()
    {
        var settings = new AppSettings();
        var sshAgent = new SshAgentHelper(Substitute.For<ILogger<SshAgentHelper>>(), null, Wsl);
        var transport = new SshRepositoryTransport(
            settings, sshAgent, Wsl, Substitute.For<ILogger<SshRepositoryTransport>>());
        _factory = new BorgServiceFactory(
            Substitute.For<ILoggerFactory>(), settings, transport, Wsl,
            Substitute.For<INetworkShareMounter>());
    }

    [Fact]
    public void GetService_Borg1_ReturnsBorg1Service()
    {
        var service = _factory.GetService(BorgVersion.Borg1);
        Assert.IsType<Borg1Service>(service);
    }

    [Fact]
    public void GetService_Borg2_ReturnsBorg2Service()
    {
        var service = _factory.GetService(BorgVersion.Borg2);
        Assert.IsType<Borg2Service>(service);
    }

    [Fact]
    public void GetService_ReturnsSameInstance_WhenCalledTwice()
    {
        var first = _factory.GetService(BorgVersion.Borg1);
        var second = _factory.GetService(BorgVersion.Borg1);
        Assert.Same(first, second);
    }

    [Fact]
    public void GetService_ReturnsSameInstance_Borg2_WhenCalledTwice()
    {
        var first = _factory.GetService(BorgVersion.Borg2);
        var second = _factory.GetService(BorgVersion.Borg2);
        Assert.Same(first, second);
    }

    [Fact]
    public void GetService_ReturnsDifferentInstances_ForDifferentVersions()
    {
        var borg1 = _factory.GetService(BorgVersion.Borg1);
        var borg2 = _factory.GetService(BorgVersion.Borg2);
        Assert.NotSame(borg1, borg2);
    }
}
