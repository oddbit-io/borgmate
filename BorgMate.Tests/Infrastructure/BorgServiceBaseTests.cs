using BorgMate.Infrastructure.Borg;

namespace BorgMate.Tests.Infrastructure;

public class BorgServiceBaseTests
{
    [Fact]
    public void ApplyDefaultEnvironment_SetsModernBorgExitCodes()
    {
        var environment = new Dictionary<string, string>();

        BorgServiceBase.ApplyDefaultEnvironment(environment);

        Assert.Equal("C", environment["LC_ALL"]);
        Assert.Equal("modern", environment["BORG_EXIT_CODES"]);
    }

    [Fact]
    public void ApplyDefaultEnvironment_OverwritesLegacyBorgExitCodeMode()
    {
        var environment = new Dictionary<string, string>
        {
            ["BORG_EXIT_CODES"] = "legacy"
        };

        BorgServiceBase.ApplyDefaultEnvironment(environment);

        Assert.Equal("modern", environment["BORG_EXIT_CODES"]);
    }
}
