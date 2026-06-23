using System.Collections.Generic;
using BorgMate.Domain.Settings;

namespace BorgMate.Persistence.Configuration;

public class AppConfigDocument
{
    public AppSettings Settings { get; set; } = new();
    public List<RepositoryData> Repositories { get; set; } = [];
    public List<BackupTaskData> Tasks { get; set; } = [];
}