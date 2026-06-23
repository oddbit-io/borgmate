using BorgMate.Domain.Settings;
using BorgMate.Infrastructure.Borg;

namespace BorgMate.Tests.Domain;

public class AppSettingsTests
{
    [Fact]
    public void Constructor_DoesNotProbeBorgPaths()
    {
        var borg1ProbeCount = 0;
        var borg2ProbeCount = 0;

        var settings = new AppSettings(
            () =>
            {
                borg1ProbeCount++;
                return new BorgPathDetectionResult("borg", null);
            },
            () =>
            {
                borg2ProbeCount++;
                return new BorgPathDetectionResult("borg2", null);
            });

        Assert.Equal(0, borg1ProbeCount);
        Assert.Equal(0, borg2ProbeCount);

        Assert.Equal("borg", settings.DefaultBorg1Path);
        Assert.Equal("borg", settings.DefaultBorg1Path);
        Assert.Equal(1, borg1ProbeCount);
        Assert.Equal(0, borg2ProbeCount);
    }
}
