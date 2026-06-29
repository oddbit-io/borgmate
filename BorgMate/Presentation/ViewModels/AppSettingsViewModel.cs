using System;
using Avalonia;
using Avalonia.Styling;
using Avalonia.Threading;
using BorgMate.Domain.Settings;
using BorgMate.Infrastructure.Platform;
using BorgMate.Persistence.Configuration;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BorgMate.Presentation.ViewModels;

public partial class AppSettingsViewModel : ViewModelBase, ISaveable
{
    private readonly IAutoStartService _autoStartService;

    private readonly AppSettings _settings;
    private readonly ISettingsStore _settingsStore;
    private readonly IWakeScheduler? _wakeScheduler;

    [ObservableProperty]
    private bool _autoLoadArchiveDetails = true;

    [ObservableProperty]
    private bool _autoLoadArchives = true;

    [ObservableProperty]
    private bool _autoLoadStats = true;

    [ObservableProperty]
    private bool _binaryUnits;

    [ObservableProperty]
    private string _borg1BinaryPath = "";

    [ObservableProperty]
    private string _borg2BinaryPath = "";

    [ObservableProperty]
    private bool _checkForUpdates;

    [ObservableProperty]
    private bool _experimentalBorg2Support;

    [ObservableProperty]
    private bool _isJournalRetentionEnabled;

    [ObservableProperty]
    private bool _isLogRetentionEnabled;

    [ObservableProperty]
    private bool _isSaved;

    [ObservableProperty]
    private bool _noAcls = true;

    [ObservableProperty]
    private bool _noFlags = true;

    [ObservableProperty]
    private bool _noXattrs = true;

    [ObservableProperty]
    private RetentionPeriod _journalRetention = RetentionPeriod.OneWeek;

    [ObservableProperty]
    private bool _loggingEnabled;

    [ObservableProperty]
    private AppLogLevel _logLevel = AppLogLevel.Info;

    [ObservableProperty]
    private RetentionPeriod _logRetention = RetentionPeriod.OneWeek;

    [ObservableProperty]
    private string _selectedLanguage = "Auto";

    [ObservableProperty]
    private AppTheme _selectedTheme;

    [ObservableProperty]
    private bool _showNotifications;

    [ObservableProperty]
    private bool _sparse;

    [ObservableProperty]
    private int _sshKeepAliveCountMax;

    [ObservableProperty]
    private int _sshKeepAliveInterval;

    [ObservableProperty]
    private bool _startAtLogin;

    [ObservableProperty]
    private bool _startMinimized;

    [ObservableProperty]
    private bool _wakeForScheduledBackups;

    [ObservableProperty]
    private bool _scheduledBackupsOnlyOnAcPower = true;

    public AppSettingsViewModel() : this(new AppSettings(), null!, null!, null)
    {
    }

    public AppSettingsViewModel(
        AppSettings settings,
        ISettingsStore settingsStore,
        IAutoStartService autoStartService,
        IWakeScheduler? wakeScheduler)
    {
        _settings = settings;
        _settingsStore = settingsStore;
        _autoStartService = autoStartService;
        _wakeScheduler = wakeScheduler;
        if (_wakeScheduler is not null)
            _wakeScheduler.StatusChanged += OnWakeSchedulerStatusChanged;
        Reload();
    }

    /// <summary>
    ///     Unsubscribes from the singleton wake scheduler; the dialog owner must call this
    ///     when the window closes, otherwise every dialog open leaks a VM instance.
    /// </summary>
    public void Detach()
    {
        if (_wakeScheduler is not null)
            _wakeScheduler.StatusChanged -= OnWakeSchedulerStatusChanged;
    }

    // Wake schedulers report status from background threads; bindings need the UI thread.
    // Internal test seam: unit tests replace this with a synchronous invoke.
    internal Action<Action> DispatchToUi = action =>
    {
        if (Dispatcher.UIThread.CheckAccess())
            action();
        else
            Dispatcher.UIThread.Post(action);
    };

    private void OnWakeSchedulerStatusChanged()
    {
        DispatchToUi(RaiseWakeMessageChanged);
    }

    private void RaiseWakeMessageChanged()
    {
        OnPropertyChanged(nameof(WakeSchedulerMessage));
        OnPropertyChanged(nameof(HasWakeSchedulerMessage));
    }

    public string DetectedBorg1Path => _settings.DefaultBorg1Path;
    public string DetectedBorg2Path => _settings.DefaultBorg2Path;
    public string DetectedBorg1PathDisplay => FormatDetectedBorgPath(_settings.DetectedBorg1Path);
    public string DetectedBorg2PathDisplay => FormatDetectedBorgPath(_settings.DetectedBorg2Path);
    public bool IsWakeSchedulingSupported => _wakeScheduler?.IsSupported ?? false;
    public bool HasWakeSchedulerMessage => !string.IsNullOrWhiteSpace(WakeSchedulerMessage);

    public string WakeSchedulerMessage
    {
        get
        {
            if (_wakeScheduler is null)
                return string.Empty;
            if (!_wakeScheduler.IsSupported)
                return Strings.Get("WakeForScheduledBackups.Unsupported");
            return _wakeScheduler.LastError ?? string.Empty;
        }
    }

    public AppTheme[] Themes { get; } = Enum.GetValues<AppTheme>();
    public string[] Languages { get; } = ["Auto", "English", "Русский"];
    public RetentionPeriod[] RetentionPeriods { get; } = Enum.GetValues<RetentionPeriod>();
    public AppLogLevel[] LogLevels { get; } = Enum.GetValues<AppLogLevel>();

    partial void OnSelectedThemeChanged(AppTheme value)
    {
        if (Application.Current is not null)
            Application.Current.RequestedThemeVariant = value switch
            {
                AppTheme.Light => ThemeVariant.Light,
                AppTheme.Dark => ThemeVariant.Dark,
                _ => ThemeVariant.Default
            };
    }

    public void Reload()
    {
        SelectedTheme = _settings.Theme;
        SelectedLanguage = Strings.CodeToDisplay(_settings.Language);
        if (!string.IsNullOrWhiteSpace(_settings.BorgBinaryPath) &&
            string.IsNullOrWhiteSpace(_settings.Borg1BinaryPath))
        {
            _settings.Borg1BinaryPath = _settings.BorgBinaryPath;
            _settings.BorgBinaryPath = null;
        }

        Borg1BinaryPath = _settings.GetEffectiveBorgPath(Domain.Repositories.BorgVersion.Borg1) ==
                          _settings.DefaultBorg1Path
            ? ""
            : _settings.GetEffectiveBorgPath(Domain.Repositories.BorgVersion.Borg1);
        Borg2BinaryPath = _settings.GetEffectiveBorgPath(Domain.Repositories.BorgVersion.Borg2) ==
                          _settings.DefaultBorg2Path
            ? ""
            : _settings.GetEffectiveBorgPath(Domain.Repositories.BorgVersion.Borg2);
        CheckForUpdates = _settings.CheckForUpdates;
        ExperimentalBorg2Support = _settings.ExperimentalBorg2Support;
        ShowNotifications = _settings.ShowNotifications;
        StartAtLogin = _settings.StartAtLogin;
        StartMinimized = _settings.StartMinimized;
        WakeForScheduledBackups = _settings.WakeForScheduledBackups;
        ScheduledBackupsOnlyOnAcPower = _settings.ScheduledBackupsOnlyOnAcPower;
        LoggingEnabled = _settings.LoggingEnabled;
        LogLevel = _settings.LogLevel;
        IsLogRetentionEnabled = _settings.LogRetention.HasValue;
        LogRetention = _settings.LogRetention ?? RetentionPeriod.OneWeek;
        IsJournalRetentionEnabled = _settings.JournalRetention.HasValue;
        JournalRetention = _settings.JournalRetention ?? RetentionPeriod.OneWeek;
        BinaryUnits = _settings.BinaryUnits;
        SshKeepAliveInterval = _settings.SshKeepAliveInterval;
        SshKeepAliveCountMax = _settings.SshKeepAliveCountMax;
        AutoLoadArchives = _settings.AutoLoadArchives;
        AutoLoadStats = _settings.AutoLoadStats;
        AutoLoadArchiveDetails = _settings.AutoLoadArchiveDetails;
        _settings.DefaultExtractOptions ??= new BorgExtractOptions();
        NoFlags = _settings.DefaultExtractOptions.NoFlags;
        NoAcls = _settings.DefaultExtractOptions.NoAcls;
        NoXattrs = _settings.DefaultExtractOptions.NoXattrs;
        Sparse = _settings.DefaultExtractOptions.Sparse;
    }

    [RelayCommand]
    private void Save()
    {
        _settings.BorgBinaryPath = null;
        _settings.Borg1BinaryPath = string.IsNullOrWhiteSpace(Borg1BinaryPath) ? null : Borg1BinaryPath;
        _settings.Borg2BinaryPath = string.IsNullOrWhiteSpace(Borg2BinaryPath) ? null : Borg2BinaryPath;
        _settings.Theme = SelectedTheme;
        _settings.CheckForUpdates = CheckForUpdates;
        _settings.ExperimentalBorg2Support = ExperimentalBorg2Support;
        _settings.ShowNotifications = ShowNotifications;
        _settings.StartAtLogin = StartAtLogin;
        if (_autoStartService is not null && _autoStartService.IsEnabled() != StartAtLogin)
            _autoStartService.SetEnabled(StartAtLogin);
        _settings.StartMinimized = StartMinimized;
        _settings.WakeForScheduledBackups = WakeForScheduledBackups;
        _settings.ScheduledBackupsOnlyOnAcPower = ScheduledBackupsOnlyOnAcPower;
        _settings.LoggingEnabled = LoggingEnabled;
        _settings.LogLevel = LogLevel;
        var langCode = SelectedLanguage == "Auto" ? "auto" : Strings.DisplayToCode(SelectedLanguage);
        _settings.Language = langCode;
        Strings.ApplyLanguageCode(langCode);
        _settings.LogRetention = IsLogRetentionEnabled ? LogRetention : null;
        _settings.JournalRetention = IsJournalRetentionEnabled ? JournalRetention : null;
        _settings.BinaryUnits = BinaryUnits;
        _settings.SshKeepAliveInterval = SshKeepAliveInterval;
        _settings.SshKeepAliveCountMax = SshKeepAliveCountMax;
        _settings.AutoLoadArchives = AutoLoadArchives;
        _settings.AutoLoadStats = AutoLoadStats;
        _settings.AutoLoadArchiveDetails = AutoLoadArchiveDetails;
        _settings.DefaultExtractOptions = new BorgExtractOptions
        {
            NoFlags = NoFlags,
            NoAcls = NoAcls,
            NoXattrs = NoXattrs,
            Sparse = Sparse
        };
        _settingsStore?.RequestSave();
        IsSaved = true;
    }

    private static string FormatDetectedBorgPath(string? path)
    {
        return string.IsNullOrWhiteSpace(path)
            ? Strings.Get("BorgPathNotDetected")
            : string.Format(Strings.Get("DetectedBorgPath"), path);
    }
}
