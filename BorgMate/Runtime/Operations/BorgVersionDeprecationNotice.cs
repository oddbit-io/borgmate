using BorgMate.Domain.Journal;
using BorgMate.Domain.Settings;
using BorgMate.Persistence.Configuration;
using BorgMate.Persistence.Journal;

namespace BorgMate.Runtime.Operations;

/// <summary>
///     Warns about a detected Borg 1 release that the next BorgMate release drops. Uses the version
///     probed at startup, so a changed Borg path is picked up on the next launch.
/// </summary>
public class BorgVersionDeprecationNotice
{
    private readonly AppSettings _settings;
    private readonly ISettingsStore _settingsStore;
    private readonly IJournalStore _journal;
    private readonly string? _deprecatedVersion;

    public BorgVersionDeprecationNotice(AppSettings settings, ISettingsStore settingsStore, IJournalStore journal,
        BorgCapabilityQuery capabilities)
    {
        _settings = settings;
        _settingsStore = settingsStore;
        _journal = journal;
        _deprecatedVersion = capabilities.Borg1Version is { IsDeprecatedBorg1: true } version
            ? version.VersionNumber
            : null;
    }

    /// <summary>Warning text for the detected Borg version, or null when it stays supported.</summary>
    public string? Message => _deprecatedVersion is null
        ? null
        : string.Format(Strings.Get("BorgVersionDeprecated.Message"), _deprecatedVersion);

    /// <summary>Journals the warning unless this Borg version was already reported.</summary>
    public void ReportOnce()
    {
        if (_deprecatedVersion is null || _deprecatedVersion == _settings.ReportedDeprecatedBorgVersion)
            return;

        _settings.ReportedDeprecatedBorgVersion = _deprecatedVersion;
        var entry = _journal.Add(JournalEventKind.BorgVersionDeprecated, [_deprecatedVersion]);
        // Complete triggers the OS notification (honoring the ShowNotifications setting).
        _journal.Complete(entry, JournalResult.CompletedWithWarnings, Message);
        _settingsStore.RequestSave();
    }
}
