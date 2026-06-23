using System.Collections.Generic;
using BorgMate.Domain.Repositories;

namespace BorgMate.Runtime.Operations;

/// <summary>Successful scheduled-backup check result.</summary>
public sealed record ScheduleCheckResult(IReadOnlyList<BorgRepository> CheckedRepositories, int TriggeredBackupCount);