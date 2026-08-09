using System;
using System.Collections.ObjectModel;

namespace BorgMate.Domain.Repositories;

/// <summary>
///     Persisted borg repository configuration.
/// </summary>
public sealed record BorgRepository
{
    /// <summary>Display name shown in the repository list and operation titles.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Optional archive name prefix; when empty, <see cref="Name" /> is used.</summary>
    public string ArchiveNamePrefix { get; set; } = string.Empty;

    /// <summary>Repository path or URL used as the stable repository identity.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>Borg 1 repository encryption mode used for initialization and passphrase prompting.</summary>
    public BorgEncryptionMode EncryptionMode { get; set; } = BorgEncryptionMode.RepokeyBlake2;

    /// <summary>Borg 2 repository encryption mode used for initialization and passphrase prompting.</summary>
    public Borg2EncryptionMode Borg2EncryptionMode { get; set; } = Borg2EncryptionMode.RepokeyAesOcb;

    /// <summary>How the repository location is represented in the editor and command layer.</summary>
    public BorgRepositoryLocationKind LocationKind { get; set; } = BorgRepositoryLocationKind.LocalPath;

    /// <summary>Raw Borg 2 repository URL when <see cref="LocationKind" /> is <see cref="BorgRepositoryLocationKind.BorgUrl" />.</summary>
    public string BorgUrl { get; set; } = string.Empty;

    /// <summary>SSH private key path for remote repositories.</summary>
    public string SshKeyPath { get; set; } = string.Empty;

    /// <summary>Upload rate limit in KiB/s. Maps to borg's rate-limit option. Zero means unlimited.</summary>
    public int RateLimit { get; set; }

    /// <summary>
    ///     Skip cloud placeholder files without local content during backup (macOS iCloud).
    ///     Maps to borg 1.4.5+ <c>create --exclude-dataless</c>; ignored by older borg.
    /// </summary>
    public bool ExcludeCloudFiles { get; set; } = true;

    /// <summary>SSH port for remote repositories.</summary>
    public int SshPort { get; set; } = 22;

    /// <summary>Borg CLI generation used for command selection.</summary>
    public BorgVersion BorgVersion { get; set; } = BorgVersion.Borg1;

    /// <summary>Optional remote borg binary path.</summary>
    public string BorgRemotePath { get; set; } = string.Empty;

    /// <summary>Legacy local flag retained for config compatibility.</summary>
    public bool IsLocal { get; set; } = true;

    /// <summary>Source directories included in backups.</summary>
    public ObservableCollection<string> SourceDirectories { get; set; } = [];

    /// <summary>Local files and directory trees excluded from backups.</summary>
    public ObservableCollection<string> ExcludedPaths { get; set; } = [];

    /// <summary>Manual or scheduled backup mode.</summary>
    public BackupMode Mode { get; set; } = BackupMode.Manual;

    /// <summary>Backup timing configuration.</summary>
    public BackupSchedule Schedule { get; set; } = new();

    /// <summary>Repository prune retention options.</summary>
    public PruneOptions PruneOptions { get; set; } = new();

    /// <summary>Last successful backup timestamp.</summary>
    public DateTime? LastBackupAt { get; set; }

    /// <summary>True when this repository uses the structured SSH fields.</summary>
    public bool IsSsh => LocationKind == BorgRepositoryLocationKind.SshFields ||
                         (!IsLocal && LocationKind is not BorgRepositoryLocationKind.BorgUrl
                             and not BorgRepositoryLocationKind.SmbShare);

    /// <summary>True when this repository requires a borg passphrase.</summary>
    public bool IsEncrypted => BorgVersion == BorgVersion.Borg2
        ? Borg2EncryptionMode != Borg2EncryptionMode.None
        : EncryptionMode != BorgEncryptionMode.None;

    /// <summary>True when scheduled backups are enabled and at least one source directory exists.</summary>
    public bool IsScheduled => Mode == BackupMode.Scheduled && SourceDirectories.Count > 0;
}
