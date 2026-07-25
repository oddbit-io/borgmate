using System.Collections.Generic;
using BorgMate.Domain.Repositories;

namespace BorgMate.Persistence.Configuration;

/// <summary>Persisted repository configuration shape stored in config.json.</summary>
public class RepositoryData
{
    public string Name { get; set; } = string.Empty;
    public string ArchiveNamePrefix { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public BorgEncryptionMode EncryptionMode { get; set; } = BorgEncryptionMode.RepokeyBlake2;
    public Borg2EncryptionMode Borg2EncryptionMode { get; set; } = Borg2EncryptionMode.RepokeyAesOcb;
    public BorgRepositoryLocationKind? LocationKind { get; set; }
    public string BorgUrl { get; set; } = string.Empty;
    public string SshKeyPath { get; set; } = string.Empty;
    public int RateLimit { get; set; }
    public int SshPort { get; set; } = 22;
    public BorgVersion BorgVersion { get; set; } = BorgVersion.Borg1;
    public string BorgRemotePath { get; set; } = string.Empty;
    public bool IsLocal { get; set; } = true;
    public List<string>? SourceDirectories { get; set; }
    public BackupScheduleData? Schedule { get; set; }
    public BackupMode? Mode { get; set; }
    public PruneOptionsData? PruneOptions { get; set; }
}
