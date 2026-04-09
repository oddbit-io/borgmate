using BorgMate.Models;
using BorgMate.Services;
using BorgMate.Services.Borg;
using BorgMate.Services.Config;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace BorgMate.Tests;

public class BorgServiceFactoryTests
{
    private static readonly WslHelper Wsl = new(Substitute.For<ILogger<WslHelper>>());
    private readonly BorgServiceFactory _factory = new(Substitute.For<ILoggerFactory>(), new AppSettings(),
        new SshAgentHelper(Substitute.For<ILogger<SshAgentHelper>>(), null, Wsl), Wsl);

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
