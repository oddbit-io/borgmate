using BorgMate.Domain.Repositories;
using BorgMate.Infrastructure.Borg;

namespace BorgMate.Tests.Infrastructure;

public class BorgPathHelperTests
{
    [Fact]
    public void Detect_Borg2MissingAndBorgReportsBorg1_ReturnsDistinctFallbackForBorg2()
    {
        var candidates = new Dictionary<string, string[]>
        {
            ["borg"] = ["borg"]
        };
        var versions = new Dictionary<string, string?>
        {
            ["borg"] = "borg 1.4.4"
        };

        var borg1 = Detect(BorgVersion.Borg1, candidates, versions);
        var borg2 = Detect(BorgVersion.Borg2, candidates, versions);

        Assert.Equal("borg", borg1.EffectivePath);
        Assert.Equal("borg", borg1.DetectedPath);
        Assert.Equal("borg2", borg2.EffectivePath);
        Assert.Null(borg2.DetectedPath);
    }

    [Fact]
    public void Detect_Borg2AndBorgReportExpectedVersions_ReturnsDistinctPaths()
    {
        var candidates = new Dictionary<string, string[]>
        {
            ["borg"] = ["/usr/bin/borg"],
            ["borg2"] = ["/usr/local/bin/borg2"]
        };
        var versions = new Dictionary<string, string?>
        {
            ["/usr/bin/borg"] = "borg 1.4.4",
            ["/usr/local/bin/borg2"] = "borg 2.0.0b21"
        };

        var borg1 = Detect(BorgVersion.Borg1, candidates, versions);
        var borg2 = Detect(BorgVersion.Borg2, candidates, versions);

        Assert.Equal("/usr/bin/borg", borg1.EffectivePath);
        Assert.Equal("/usr/bin/borg", borg1.DetectedPath);
        Assert.Equal("/usr/local/bin/borg2", borg2.EffectivePath);
        Assert.Equal("/usr/local/bin/borg2", borg2.DetectedPath);
    }

    [Fact]
    public void Detect_Borg2MissingAndBorgReportsBorg2_ReturnsBorgForBorg2()
    {
        var candidates = new Dictionary<string, string[]>
        {
            ["borg"] = ["borg"]
        };
        var versions = new Dictionary<string, string?>
        {
            ["borg"] = "borg 2.0.0b21"
        };

        var result = Detect(BorgVersion.Borg2, candidates, versions);

        Assert.Equal("borg", result.EffectivePath);
        Assert.Equal("borg", result.DetectedPath);
    }

    [Fact]
    public void Detect_WrongMajorCandidatesAreSkipped()
    {
        var candidates = new Dictionary<string, string[]>
        {
            ["borg"] = ["borg"],
            ["borg1"] = ["borg1"]
        };
        var versions = new Dictionary<string, string?>
        {
            ["borg"] = "borg 2.0.0b21",
            ["borg1"] = "borg 1.4.4"
        };

        var result = Detect(BorgVersion.Borg1, candidates, versions);

        Assert.Equal("borg1", result.EffectivePath);
        Assert.Equal("borg1", result.DetectedPath);
    }

    [Fact]
    public void Detect_UnparsableOrFailedVersionProbesAreIgnored()
    {
        var candidates = new Dictionary<string, string[]>
        {
            ["borg"] = ["bad-borg", "missing-borg", "borg"],
            ["borg2"] = ["bad-borg2", "missing-borg2"]
        };
        var versions = new Dictionary<string, string?>
        {
            ["bad-borg"] = "not borg",
            ["bad-borg2"] = "not borg",
            ["missing-borg"] = null,
            ["missing-borg2"] = null,
            ["borg"] = "borg 2.0.0b21"
        };

        var result = Detect(BorgVersion.Borg2, candidates, versions);

        Assert.Equal("borg", result.EffectivePath);
        Assert.Equal("borg", result.DetectedPath);
    }

    private static BorgPathDetectionResult Detect(
        BorgVersion borgVersion,
        Dictionary<string, string[]> candidates,
        Dictionary<string, string?> versions)
    {
        return BorgPathHelper.DetectWithStatus(
            borgVersion,
            binary => candidates.TryGetValue(binary, out var values) ? values : [],
            candidate => versions.TryGetValue(candidate, out var versionText) && versionText is not null
                ? BorgCliVersion.Parse(versionText)
                : null);
    }
}
