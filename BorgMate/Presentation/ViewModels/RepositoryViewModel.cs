using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using BorgMate.Domain.Archives;
using BorgMate.Domain.Repositories;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BorgMate.Presentation.ViewModels;

/// <summary>
///     Presentation wrapper for a persisted <see cref="BorgRepository" />.
/// </summary>
public sealed partial class RepositoryViewModel : ObservableObject
{
    [ObservableProperty]
    private double? _commandProgress;

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isLoadingStats;

    [ObservableProperty]
    private string? _lastError;

    [ObservableProperty]
    private long? _statsTotalChunks;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStats))]
    private long? _statsTotalSize;

    /// <summary>Creates a presentation repository with a new persisted model.</summary>
    public RepositoryViewModel() : this(new BorgRepository())
    {
    }

    /// <summary>Creates a presentation repository that wraps <paramref name="model" />.</summary>
    public RepositoryViewModel(BorgRepository model)
    {
        Model = model;
    }

    /// <summary>The persisted repository configuration.</summary>
    public BorgRepository Model { get; }

    public string Name
    {
        get => Model.Name;
        set => SetModelProperty(Model.Name, value, static (m, v) => m.Name = v);
    }

    public string ArchiveNamePrefix
    {
        get => Model.ArchiveNamePrefix;
        set => SetModelProperty(Model.ArchiveNamePrefix, value, static (m, v) => m.ArchiveNamePrefix = v);
    }

    public string Path
    {
        get => Model.Path;
        set => SetModelProperty(Model.Path, value, static (m, v) => m.Path = v);
    }

    public BorgEncryptionMode EncryptionMode
    {
        get => Model.EncryptionMode;
        set
        {
            if (SetModelProperty(Model.EncryptionMode, value, static (m, v) => m.EncryptionMode = v))
                OnPropertyChanged(nameof(IsEncrypted));
        }
    }

    public Borg2EncryptionMode Borg2EncryptionMode
    {
        get => Model.Borg2EncryptionMode;
        set
        {
            if (SetModelProperty(Model.Borg2EncryptionMode, value, static (m, v) => m.Borg2EncryptionMode = v))
                OnPropertyChanged(nameof(IsEncrypted));
        }
    }

    public BorgRepositoryLocationKind LocationKind
    {
        get => Model.LocationKind;
        set
        {
            if (SetModelProperty(Model.LocationKind, value, static (m, v) => m.LocationKind = v))
            {
                OnPropertyChanged(nameof(IsLocal));
                OnPropertyChanged(nameof(IsSsh));
            }
        }
    }

    public string BorgUrl
    {
        get => Model.BorgUrl;
        set => SetModelProperty(Model.BorgUrl, value, static (m, v) => m.BorgUrl = v);
    }

    public string SshKeyPath
    {
        get => Model.SshKeyPath;
        set => SetModelProperty(Model.SshKeyPath, value, static (m, v) => m.SshKeyPath = v);
    }

    public int RateLimit
    {
        get => Model.RateLimit;
        set => SetModelProperty(Model.RateLimit, value, static (m, v) => m.RateLimit = v);
    }

    public int SshPort
    {
        get => Model.SshPort;
        set => SetModelProperty(Model.SshPort, value, static (m, v) => m.SshPort = v);
    }

    public BorgVersion BorgVersion
    {
        get => Model.BorgVersion;
        set => SetModelProperty(Model.BorgVersion, value, static (m, v) => m.BorgVersion = v);
    }

    public string BorgRemotePath
    {
        get => Model.BorgRemotePath;
        set => SetModelProperty(Model.BorgRemotePath, value, static (m, v) => m.BorgRemotePath = v);
    }

    public bool IsLocal
    {
        get => Model.IsLocal;
        set
        {
            if (SetModelProperty(Model.IsLocal, value, static (m, v) => m.IsLocal = v))
            {
                Model.LocationKind = value
                    ? BorgRepositoryLocationKind.LocalPath
                    : BorgRepositoryLocationKind.SshFields;
                OnPropertyChanged(nameof(LocationKind));
                OnPropertyChanged(nameof(IsSsh));
            }
        }
    }

    public bool IsSsh => Model.IsSsh;

    public bool IsEncrypted => Model.IsEncrypted;

    public ObservableCollection<string> SourceDirectories => Model.SourceDirectories;

    public BackupMode Mode
    {
        get => Model.Mode;
        set
        {
            if (SetModelProperty(Model.Mode, value, static (m, v) => m.Mode = v))
                OnPropertyChanged(nameof(IsScheduled));
        }
    }

    public BackupSchedule Schedule
    {
        get => Model.Schedule;
        set => SetModelProperty(Model.Schedule, value, static (m, v) => m.Schedule = v);
    }

    public PruneOptions PruneOptions
    {
        get => Model.PruneOptions;
        set => SetModelProperty(Model.PruneOptions, value, static (m, v) => m.PruneOptions = v);
    }

    public DateTime? LastBackupAt
    {
        get => Model.LastBackupAt;
        set => SetModelProperty(Model.LastBackupAt, value, static (m, v) => m.LastBackupAt = v);
    }

    public bool IsScheduled => Model.IsScheduled;

    /// <summary>Archive list cached by presentation after a successful query.</summary>
    public List<BorgArchive>? LoadedArchives { get; set; }

    public bool HasStats => StatsTotalSize is not null;

    public static implicit operator BorgRepository(RepositoryViewModel repo)
    {
        return repo.Model;
    }

    public static implicit operator RepositoryViewModel(BorgRepository repo)
    {
        return new RepositoryViewModel(repo);
    }

    /// <summary>Fires change notifications for schedule display converters.</summary>
    public void RefreshScheduleDisplay()
    {
        OnPropertyChanged(nameof(IsScheduled));
        OnPropertyChanged(nameof(LastBackupAt));
    }

    /// <summary>Fires change notifications for every persisted field exposed by this wrapper.</summary>
    public void RefreshPersistedProperties()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(ArchiveNamePrefix));
        OnPropertyChanged(nameof(Path));
        OnPropertyChanged(nameof(EncryptionMode));
        OnPropertyChanged(nameof(Borg2EncryptionMode));
        OnPropertyChanged(nameof(LocationKind));
        OnPropertyChanged(nameof(BorgUrl));
        OnPropertyChanged(nameof(IsEncrypted));
        OnPropertyChanged(nameof(SshKeyPath));
        OnPropertyChanged(nameof(RateLimit));
        OnPropertyChanged(nameof(SshPort));
        OnPropertyChanged(nameof(BorgVersion));
        OnPropertyChanged(nameof(BorgRemotePath));
        OnPropertyChanged(nameof(IsLocal));
        OnPropertyChanged(nameof(IsSsh));
        OnPropertyChanged(nameof(SourceDirectories));
        OnPropertyChanged(nameof(Mode));
        OnPropertyChanged(nameof(Schedule));
        OnPropertyChanged(nameof(PruneOptions));
        RefreshScheduleDisplay();
    }

    /// <summary>Fires change notifications for localized stats converters.</summary>
    public void RefreshStats()
    {
        OnPropertyChanged(nameof(StatsTotalSize));
        OnPropertyChanged(nameof(StatsTotalChunks));
    }

    private bool SetModelProperty<T>(
        T currentValue,
        T newValue,
        Action<BorgRepository, T> apply,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(currentValue, newValue))
            return false;

        apply(Model, newValue);
        OnPropertyChanged(propertyName);
        return true;
    }
}
