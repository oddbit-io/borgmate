using CommunityToolkit.Mvvm.ComponentModel;

namespace BorgMate.Domain.Repositories;

public partial class PruneOptions : ObservableObject
{
    [ObservableProperty]
    private bool _compactAfterPrune = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAnyRetention))]
    private int _keepDaily;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAnyRetention))]
    private int _keepHourly;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAnyRetention))]
    private int _keepLast;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAnyRetention))]
    private int _keepMonthly;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAnyRetention))]
    private int _keepWeekly;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAnyRetention))]
    private int _keepYearly;

    public bool HasAnyRetention =>
        KeepLast > 0 || KeepHourly > 0 || KeepDaily > 0 ||
        KeepWeekly > 0 || KeepMonthly > 0 || KeepYearly > 0;
}