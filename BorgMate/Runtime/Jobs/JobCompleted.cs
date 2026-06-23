using BorgMate.Infrastructure.Borg;

namespace BorgMate.Runtime.Jobs;

/// <summary>Published after a queued borg job completes with its raw result.</summary>
public sealed record JobCompleted(BorgJob Job, BorgResult Result);