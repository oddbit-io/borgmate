using System;
using System.ComponentModel;
using System.Globalization;
using System.Resources;
using BorgMate.Services.Config;

namespace BorgMate.Localization;

/// <summary>
/// Centralized string resources for i18n. Loads translations from embedded .resx files.
/// Code access: Strings.Get("key") or Strings.Instance["key"]
/// AXAML access: {Binding [Key], Source={x:Static loc:Strings.Instance}}
/// </summary>
public class Strings : INotifyPropertyChanged
{
    public static Strings Instance { get; } = new();

    private static AppSettings? _settings;
    public static CultureInfo Culture { get; private set; } = CultureInfo.InvariantCulture;

    private static readonly ResourceManager Rm =
        new("BorgMate.Localization.Resources", typeof(Strings).Assembly);

    public static void Initialize(AppSettings settings) => _settings = settings;

    public event PropertyChangedEventHandler? PropertyChanged;

    public static event Action? LanguageChanged;

    /// <summary>Indexer for AXAML compiled bindings: {Binding [Key]}</summary>
    public string this[string key] => Get(key);

    public void NotifyAll() =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));

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

    public static string DisplayToCode(string display) => display switch
    {
        "English" => "en",
        "Русский" => "ru",
        _ => "en"
    };

    public static string CodeToDisplay(string code) => code switch
    {
        "en" => "English",
        "ru" => "Русский",
        _ => "Auto"
    };

    public static void ApplyLanguageCode(string code)
    {
        if (code is "auto" or "" or "Auto")
            DetectLanguage();
        else
            SetLanguage(code);
    }

    public static string Get(string key) =>
        Rm.GetString(key, Culture) ?? key;

    private static readonly string[] ByteSuffixKeysBin = ["BytesB", "BytesKB.bin", "BytesMB.bin", "BytesGB.bin", "BytesTB.bin"];
    private static readonly string[] ByteSuffixKeysDec = ["BytesB", "BytesKB.dec", "BytesMB.dec", "BytesGB.dec", "BytesTB.dec"];

    private static string[] ByteSuffixKeys => _settings?.BinaryUnits != false ? ByteSuffixKeysBin : ByteSuffixKeysDec;
    private static int ByteBase => _settings?.BinaryUnits != false ? 1024 : 1000;

    public static string FormatBytes(long bytes)
    {
        var (value, unitIndex) = GetByteUnit(bytes);
        return $"{value.ToString(unitIndex == 0 ? "N0" : "0.0", Culture)} {Get(ByteSuffixKeys[unitIndex])}";
    }

    /// <summary>
    /// Formats bytes using the same unit as referenceBytes, so both strings have the same suffix.
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
        return $"{value.ToString(unitIndex == 0 ? "N0" : "0.0", Culture)} {Get(ByteSuffixKeys[unitIndex])}/{Get("SpeedPerSec")}";
    }

    private static (double value, int unitIndex) GetByteUnit(long bytes)
    {
        var @base = ByteBase;
        double value = bytes;
        var i = 0;
        while (value >= @base && i < ByteSuffixKeysBin.Length - 1) { value /= @base; i++; }
        return (value, i);
    }
}
