using Avalonia;
using Avalonia.Styling;
using BorgMate.Models;
using BorgMate.Services.Config;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BorgMate.ViewModels;

public partial class AppSettingsViewModel : ViewModelBase, ISaveable
{
    [ObservableProperty]
    private bool _isSaved;

    private readonly AppSettings _settings;
    private readonly IConfigService _configService;
    private readonly IAutoStartService _autoStartService;

    public AppSettingsViewModel() : this(new AppSettings(), null!, null!) { }

    public AppSettingsViewModel(AppSettings settings, IConfigService configService, IAutoStartService autoStartService)
    {
        _settings = settings;
        _configService = configService;
        _autoStartService = autoStartService;
        Reload();
    }

    [ObservableProperty]
    private AppTheme _selectedTheme;

    [ObservableProperty]
    private bool _checkForUpdates;

    [ObservableProperty]
    private bool _showNotifications;

    [ObservableProperty]
    private bool _startAtLogin;

    [ObservableProperty]
    private bool _startMinimized;

    [ObservableProperty]
    private string _selectedLanguage = "Auto";

    public string DetectedBorgPath => _settings.DefaultBorgPath;
    public string DetectedBorgPathDisplay => string.Format(Strings.Get("DetectedBorgPath"), _settings.DefaultBorgPath);

    [ObservableProperty]
    private string _borgBinaryPath = "";

    [ObservableProperty]
    private bool _loggingEnabled;

    [ObservableProperty]
    private bool _isLogRetentionEnabled;

    [ObservableProperty]
    private RetentionPeriod _logRetention = RetentionPeriod.OneWeek;

    [ObservableProperty]
    private bool _isJournalRetentionEnabled;

    [ObservableProperty]
    private RetentionPeriod _journalRetention = RetentionPeriod.OneWeek;

    [ObservableProperty]
    private bool _binaryUnits;

    [ObservableProperty]
    private int _sshKeepAliveInterval;

    [ObservableProperty]
    private int _sshKeepAliveCountMax;

    public AppTheme[] Themes { get; } = System.Enum.GetValues<AppTheme>();
    public string[] Languages { get; } = ["Auto", "English", "Русский"];
    public RetentionPeriod[] RetentionPeriods { get; } = System.Enum.GetValues<RetentionPeriod>();

    partial void OnSelectedThemeChanged(AppTheme value)
    {
        if (Application.Current is not null)
        {
            Application.Current.RequestedThemeVariant = value switch
            {
                AppTheme.Light => ThemeVariant.Light,
                AppTheme.Dark => ThemeVariant.Dark,
                _ => ThemeVariant.Default
            };
        }
    }

    public void Reload()
    {
        SelectedTheme = _settings.Theme;
        SelectedLanguage = Strings.CodeToDisplay(_settings.Language);
        BorgBinaryPath = _settings.EffectiveBorgPath == _settings.DefaultBorgPath ? "" : _settings.EffectiveBorgPath;
        CheckForUpdates = _settings.CheckForUpdates;
        ShowNotifications = _settings.ShowNotifications;
        StartAtLogin = _settings.StartAtLogin;
        StartMinimized = _settings.StartMinimized;
        LoggingEnabled = _settings.LoggingEnabled;
        IsLogRetentionEnabled = _settings.LogRetention.HasValue;
        LogRetention = _settings.LogRetention ?? RetentionPeriod.OneWeek;
        IsJournalRetentionEnabled = _settings.JournalRetention.HasValue;
        JournalRetention = _settings.JournalRetention ?? RetentionPeriod.OneWeek;
        BinaryUnits = _settings.BinaryUnits;
        SshKeepAliveInterval = _settings.SshKeepAliveInterval;
        SshKeepAliveCountMax = _settings.SshKeepAliveCountMax;
    }

    [RelayCommand]
    private void Save()
    {
        _settings.BorgBinaryPath = string.IsNullOrWhiteSpace(BorgBinaryPath) ? null : BorgBinaryPath;
        _settings.Theme = SelectedTheme;
        _settings.CheckForUpdates = CheckForUpdates;
        _settings.ShowNotifications = ShowNotifications;
        _settings.StartAtLogin = StartAtLogin;
        _autoStartService?.SetEnabled(StartAtLogin);
        _settings.StartMinimized = StartMinimized;
        _settings.LoggingEnabled = LoggingEnabled;
        var langCode = SelectedLanguage == "Auto" ? "auto" : Strings.DisplayToCode(SelectedLanguage);
        _settings.Language = langCode;
        Strings.ApplyLanguageCode(langCode);
        _settings.LogRetention = IsLogRetentionEnabled ? LogRetention : null;
        _settings.JournalRetention = IsJournalRetentionEnabled ? JournalRetention : null;
        _settings.BinaryUnits = BinaryUnits;
        _settings.SshKeepAliveInterval = SshKeepAliveInterval;
        _settings.SshKeepAliveCountMax = SshKeepAliveCountMax;
        _configService?.RequestSave();
        IsSaved = true;
    }
}
