using BorgMate.Domain.Journal;
using BorgMate.Domain.Settings;
using BorgMate.Infrastructure.Borg;
using BorgMate.Persistence.Configuration;
using BorgMate.Persistence.Journal;
using BorgMate.Runtime.Operations;
using NSubstitute;
using JournalEntry = BorgMate.Domain.Journal.JournalEntry;

namespace BorgMate.Tests.Runtime;

public class BorgVersionDeprecationNoticeTests
{
    private readonly ISettingsStore _settingsStore = Substitute.For<ISettingsStore>();
    private readonly IJournalStore _journal = Substitute.For<IJournalStore>();

    public BorgVersionDeprecationNoticeTests()
    {
        _journal.Add(Arg.Any<JournalEventKind>(), Arg.Any<object[]?>(), Arg.Any<string?>())
            .Returns(call => new JournalEntry(call.Arg<JournalEventKind>(), call.Arg<object[]?>()));
    }

    [Fact]
    public void OldBorg_ShowsWarningAndJournalsOnce()
    {
        var settings = CreateSettings("borg 1.4.4");
        var notice = new BorgVersionDeprecationNotice(settings, _settingsStore, _journal, new BorgCapabilityQuery(settings));

        notice.ReportOnce();
        notice.ReportOnce();

        const string message =
            "Borg 1.4.4 will not be supported by the next BorgMate version. Update Borg to 1.4.5 or newer.";
        Assert.Equal(message, notice.Message);
        _journal.Received(1).Add(JournalEventKind.BorgVersionDeprecated,
            Arg.Is<object[]?>(args => args != null && (string)args[0] == "1.4.4"), Arg.Any<string?>());
        _journal.Received(1).Complete(Arg.Any<JournalEntry>(), JournalResult.CompletedWithWarnings, message);
        Assert.Equal("1.4.4", settings.ReportedDeprecatedBorgVersion);
        _settingsStore.Received(1).RequestSave();
    }

    [Fact]
    public void AlreadyReportedVersion_ShowsWarningWithoutJournal()
    {
        var settings = CreateSettings("borg 1.4.4");
        settings.ReportedDeprecatedBorgVersion = "1.4.4";
        var notice = new BorgVersionDeprecationNotice(settings, _settingsStore, _journal, new BorgCapabilityQuery(settings));

        notice.ReportOnce();

        Assert.NotNull(notice.Message);
        _journal.DidNotReceiveWithAnyArgs().Add(default);
        _settingsStore.DidNotReceive().RequestSave();
    }

    [Fact]
    public void NewDeprecatedVersion_IsReportedAgain()
    {
        var settings = CreateSettings("borg 1.2.8");
        settings.ReportedDeprecatedBorgVersion = "1.4.4";
        var notice = new BorgVersionDeprecationNotice(settings, _settingsStore, _journal, new BorgCapabilityQuery(settings));

        notice.ReportOnce();

        _journal.Received(1).Add(JournalEventKind.BorgVersionDeprecated,
            Arg.Is<object[]?>(args => args != null && (string)args[0] == "1.2.8"), Arg.Any<string?>());
    }

    [Theory]
    [InlineData("borg 1.4.5")]
    [InlineData("borg 2.0.0b21")]
    [InlineData(null)]
    public void SupportedOrUndetectedBorg_ShowsNothing(string? versionText)
    {
        var settings = CreateSettings(versionText);
        var notice = new BorgVersionDeprecationNotice(settings, _settingsStore, _journal, new BorgCapabilityQuery(settings));

        notice.ReportOnce();

        Assert.Null(notice.Message);
        _journal.DidNotReceiveWithAnyArgs().Add(default);
    }

    [Theory]
    [InlineData("borg 1.4.5", "borg 1.4.4", true)]
    [InlineData("borg 1.4.4", "borg 1.4.5", false)]
    [InlineData("borg 1.4.4", null, false)]
    public void ConfiguredBinary_DeterminesWarningAndCapabilities(string detected, string? configured, bool deprecated)
    {
        var settings = CreateSettings(detected);
        settings.Borg1BinaryPath = "/custom/borg";
        var capabilities = new BorgCapabilityQuery(settings, path =>
        {
            Assert.Equal("/custom/borg", path);
            return configured is null ? null : BorgCliVersion.Parse(configured);
        });
        var notice = new BorgVersionDeprecationNotice(settings, _settingsStore, _journal, capabilities);

        notice.ReportOnce();

        Assert.Equal(deprecated, notice.Message is not null);
        if (deprecated)
            Assert.Contains("1.4.4", notice.Message);
        Assert.Equal(configured == "borg 1.4.5", capabilities.SupportsExcludeDataless);
        _journal.Received(deprecated ? 1 : 0).Add(JournalEventKind.BorgVersionDeprecated,
            Arg.Any<object[]?>(), Arg.Any<string?>());
    }

    private static AppSettings CreateSettings(string? versionText)
    {
        var version = versionText is null ? null : BorgCliVersion.Parse(versionText);
        return new AppSettings(
            new BorgPathDetectionResult("borg", version is null ? null : "borg", version),
            new BorgPathDetectionResult("borg2", null, null));
    }
}
