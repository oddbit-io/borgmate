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
        var borg1Version = new BorgCliVersion(1, 4, 5, null, false, "borg 1.4.5");

        var settings = new AppSettings(
            () =>
            {
                borg1ProbeCount++;
                return new BorgPathDetectionResult("borg", "borg", borg1Version);
            },
            () =>
            {
                borg2ProbeCount++;
                return new BorgPathDetectionResult("borg2", null, null);
            });

        Assert.Equal(0, borg1ProbeCount);
        Assert.Equal(0, borg2ProbeCount);

        Assert.Equal("borg", settings.DefaultBorg1Path);
        Assert.Equal("borg", settings.DefaultBorg1Path);
        Assert.Same(borg1Version, settings.DetectedBorg1Version);
        Assert.Equal(1, borg1ProbeCount);
        Assert.Equal(0, borg2ProbeCount);
    }
}
