using System;
using System.ComponentModel;
using System.Globalization;
using System.Resources;
using BorgMate.Domain.Journal;
using BorgMate.Domain.Settings;

namespace BorgMate.Localization;

/// <summary>
///     Centralized string resources for i18n. Loads translations from embedded .resx files.
///     Code access: Strings.Get("key") or Strings.Instance["key"]
///     AXAML access: {Binding [Key], Source={x:Static loc:Strings.Instance}}
/// </summary>
public class Strings : INotifyPropertyChanged
{
    private static AppSettings? _settings;

    private static readonly ResourceManager Rm =
        new("BorgMate.Localization.Resources", typeof(Strings).Assembly);

    private static readonly string[] ByteSuffixKeysBin =
        ["BytesB", "BytesKB.bin", "BytesMB.bin", "BytesGB.bin", "BytesTB.bin"];

    private static readonly string[] ByteSuffixKeysDec =
        ["BytesB", "BytesKB.dec", "BytesMB.dec", "BytesGB.dec", "BytesTB.dec"];

    // --- Journal entry formatting (used by NotificationService + AXAML converters) ---

    private static readonly string[] EventKindKeys =
    [
        "Notif.Backup", "Notif.Prune", "Notif.Check", "Notif.Compact",
        "Notif.Delete", "Notif.Create", "Notif.PassphraseFailed", "Notif.Restore",
        "Notif.ChangePassphrase"
    ];

    public static Strings Instance { get; } = new();
    public static CultureInfo Culture { get; private set; } = CultureInfo.InvariantCulture;

    /// <summary>Indexer for AXAML compiled bindings: {Binding [Key]}</summary>
    public string this[string key] => Get(key);

    private static string[] ByteSuffixKeys => _settings?.BinaryUnits != false ? ByteSuffixKeysBin : ByteSuffixKeysDec;
    private static int ByteBase => _settings?.BinaryUnits != false ? 1024 : 1000;

    public event PropertyChangedEventHandler? PropertyChanged;

    public static void Initialize(AppSettings settings)
    {
        _settings = settings;
    }

    public static event Action? LanguageChanged;

    /// <summary>
    ///     Notifies every subscriber that every bound string may have changed.
    ///     Fires the indexer-changed convention ("Item[]") first and then the INPC
    ///     all-properties convention (null) — Avalonia's compiled bindings only
    ///     subscribe for the exact property path they bind to, so the null-name
    ///     raise is what makes `[key]` bindings across the already-instantiated
    ///     UI re-read from the new culture. Without it, bindings that evaluated
    ///     before the language switch keep the old translations.
    /// </summary>
    public void NotifyAll()
    {
        var handler = PropertyChanged;
        if (handler is null) return;
        handler(this, new PropertyChangedEventArgs("Item[]"));
        handler(this, new PropertyChangedEventArgs(null));
    }

    public static void SetLanguage(string twoLetterCode)
    {
        Culture = new CultureInfo(twoLetterCode);
        Instance.NotifyAll();
        LanguageChanged?.Invoke();
    }

    public static void DetectLanguage()
    {
        SetLanguage(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);
    }

    public static string DisplayToCode(string display)
    {
        return display switch
        {
            "English" => "en",
            "Русский" => "ru",
            _ => "en"
        };
    }

    public static string CodeToDisplay(string code)
    {
        return code switch
        {
            "en" => "English",
            "ru" => "Русский",
            _ => "Auto"
        };
    }

    public static void ApplyLanguageCode(string code)
    {
        if (code is "auto" or "" or "Auto")
            DetectLanguage();
        else
            SetLanguage(code);
    }

    public static string Get(string key)
    {
        return Rm.GetString(key, Culture) ?? key;
    }

    public static string FormatBytes(long bytes)
    {
        var (value, unitIndex) = GetByteUnit(bytes);
        return $"{value.ToString(unitIndex == 0 ? "N0" : "0.0", Culture)} {Get(ByteSuffixKeys[unitIndex])}";
    }

    /// <summary>
    ///     Formats bytes using the same unit as referenceBytes, so both strings have the same suffix.
    /// </summary>
    public static string FormatBytesInUnit(long bytes, long referenceBytes)
    {
        var (_, unitIndex) = GetByteUnit(referenceBytes);
        var divisor = Math.Pow(ByteBase, unitIndex);
        var value = bytes / divisor;
        return $"{value.ToString(unitIndex == 0 ? "N0" : "0.0", Culture)} {Get(ByteSuffixKeys[unitIndex])}";
    }

    public static string FormatSpeed(long bytesPerSec)
    {
        var (value, unitIndex) = GetByteUnit(bytesPerSec);
        return
            $"{value.ToString(unitIndex == 0 ? "N0" : "0.0", Culture)} {Get(ByteSuffixKeys[unitIndex])}/{Get("SpeedPerSec")}";
    }

    private static (double value, int unitIndex) GetByteUnit(long bytes)
    {
        var @base = ByteBase;
        double value = bytes;
        var i = 0;
        while (value >= @base && i < ByteSuffixKeysBin.Length - 1)
        {
            value /= @base;
            i++;
        }

        return (value, i);
    }

    public static string FormatJournalTitle(JournalEventKind kind, object[]? titleArgs)
    {
        return titleArgs is { Length: > 0 }
            ? string.Format(Get(EventKindKeys[(int)kind]), titleArgs)
            : Get(EventKindKeys[(int)kind]);
    }

    public static string FormatJournalResult(JournalResult result)
    {
        return result switch
        {
            JournalResult.Running => Get("Job.Running"),
            JournalResult.Completed => Get("Job.Completed"),
            JournalResult.Failed => Get("Job.Failed"),
            JournalResult.Cancelled => Get("Job.Cancelled"),
            _ => ""
        };
    }
}