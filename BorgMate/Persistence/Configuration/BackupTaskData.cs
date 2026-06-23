using System.Collections.Generic;
using BorgMate.Domain.Repositories;

namespace BorgMate.Persistence.Configuration;

/// <summary>Legacy format. Migrated to BorgRepository source directories on first load.</summary>
public class BackupTaskData
{
    public string Name { get; set; } = string.Empty;
    public string RepositoryPath { get; set; } = string.Empty;
    public BackupMode Mode { get; set; } = BackupMode.Manual;
    public BackupScheduleData Schedule { get; set; } = new();
    public List<string> SourceDirectories { get; set; } = [];
}