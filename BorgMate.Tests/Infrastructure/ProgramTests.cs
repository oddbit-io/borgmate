namespace BorgMate.Tests.Infrastructure;

public class ProgramTests
{
    [Fact]
    public void ParseScheduledWakeMode_True_ForScheduledWake()
    {
        Assert.True(Program.ParseScheduledWakeMode(["--scheduled-wake"]));
    }

    [Fact]
    public void ParseScheduledWakeMode_False_ForNormalLaunch()
    {
        Assert.False(Program.ParseScheduledWakeMode([]));
    }
}
