using System;

namespace BorgMate.Domain.Repositories;

/// <summary>Capacity snapshot for the volume containing a repository.</summary>
public class StorageSpaceSnapshot
{
    public required long CapacityBytes { get; init; }

    public required long AvailableBytes { get; init; }

    public long UsedBytes => IsValid
        ? CapacityBytes - AvailableBytes
        : throw new InvalidOperationException("Storage capacity snapshot is invalid.");

    public bool IsValid => CapacityBytes > 0 && AvailableBytes >= 0 && AvailableBytes <= CapacityBytes;
}
