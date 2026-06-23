namespace BorgMate.Runtime.Progress;

/// <summary>Neutral progress information emitted by runtime operations for presentation to localize.</summary>
public sealed record OperationProgressUpdate
{
    /// <summary>Determinate progress from 0 through 100, or null when progress is indeterminate.</summary>
    public double? Percent { get; init; }

    /// <summary>Raw borg byte counters when the operation reports them.</summary>
    public ByteCounters? Counters { get; init; }

    /// <summary>Transient retry countdown state when the operation is waiting before a retry.</summary>
    public RetryCountdown? Retry { get; init; }

    /// <summary>Neutral status token that presentation maps to localized text.</summary>
    public string? StatusKey { get; init; }
}