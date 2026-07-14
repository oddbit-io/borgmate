using BorgMate.Domain.Repositories;

namespace BorgMate.Tests.Domain;

public class StorageSpaceSnapshotTests
{
    [Fact]
    public void UsedBytes_IsComputedForValidSnapshot()
    {
        var snapshot = new StorageSpaceSnapshot { CapacityBytes = 100, AvailableBytes = 40 };

        Assert.True(snapshot.IsValid);
        Assert.Equal(60, snapshot.UsedBytes);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(100, -1)]
    [InlineData(100, 101)]
    public void UsedBytes_RejectsInvalidSnapshot(long capacity, long available)
    {
        var snapshot = new StorageSpaceSnapshot
        {
            CapacityBytes = capacity,
            AvailableBytes = available
        };

        Assert.False(snapshot.IsValid);
        Assert.Throws<InvalidOperationException>(() => _ = snapshot.UsedBytes);
    }
}
