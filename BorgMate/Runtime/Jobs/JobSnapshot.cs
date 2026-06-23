using System.Collections.Generic;

namespace BorgMate.Runtime.Jobs;

/// <summary>Current aggregate queue state exposed to presentation code.</summary>
public sealed record JobSnapshot(
    IReadOnlyList<BorgJob> Jobs,
    int PendingCount,
    bool HasRunningJobs,
    bool HasPendingCommand);