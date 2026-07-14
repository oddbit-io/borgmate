using BorgMate.Domain.Repositories;
using BorgMate.Localization;
using BorgMate.Presentation.ViewModels;

namespace BorgMate.Tests.Presentation;

public class RepositoryViewModelTests
{
    [Theory]
    [InlineData(20)]
    [InlineData(40)]
    public void StorageGauge_UsesOnlyRepositorySize(long available)
    {
        var repository = CreateRepository(repositorySize: 30, capacity: 100, available: available);

        Assert.True(repository.HasDiskSpace);
        Assert.Equal(30, repository.StatsTotalSize);
        Assert.StartsWith(Strings.Get("DiskLegendRepo"), repository.DiskLegendRepoText);
        Assert.Equal(
            string.Format(Strings.Get("DiskLegendFree"), Strings.FormatBytes(available)),
            repository.DiskLegendFreeText);
    }

    [Fact]
    public void StorageGauge_IsUnavailable_WithoutSnapshot()
    {
        var repository = new RepositoryViewModel { StatsTotalSize = 30 };

        Assert.False(repository.HasDiskSpace);
    }

    [Fact]
    public void RefreshStats_NotifiesLocalizedGaugeLabels()
    {
        var repository = CreateRepository(repositorySize: 30, capacity: 100, available: 40);
        var changed = new List<string?>();
        repository.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        repository.RefreshStats();

        Assert.Contains(nameof(RepositoryViewModel.DiskLegendRepoText), changed);
        Assert.Contains(nameof(RepositoryViewModel.DiskLegendFreeText), changed);
    }

    private static RepositoryViewModel CreateRepository(long repositorySize, long capacity, long available)
    {
        return new RepositoryViewModel
        {
            StatsTotalSize = repositorySize,
            StorageSpace = new StorageSpaceSnapshot
            {
                CapacityBytes = capacity,
                AvailableBytes = available
            }
        };
    }
}
