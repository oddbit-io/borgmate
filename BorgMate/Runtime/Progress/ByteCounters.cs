namespace BorgMate.Runtime.Progress;

/// <summary>Raw borg byte and file counters reported while an operation is running.</summary>
public sealed record ByteCounters(
    long OriginalBytes,
    long CompressedBytes,
    long DeduplicatedBytes,
    long FileCount);