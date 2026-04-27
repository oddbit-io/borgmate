using CommunityToolkit.Mvvm.ComponentModel;

namespace BorgMate.Models;

public partial class PruneOptions : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAnyRetention))]
    private int _keepLast;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAnyRetention))]
    private int _keepHourly;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAnyRetention))]
    private int _keepDaily;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAnyRetention))]
    private int _keepWeekly;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAnyRetention))]
    private int _keepMonthly;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAnyRetention))]
    private int _keepYearly;

    [ObservableProperty]
    private bool _compactAfterPrune = true;

    public bool HasAnyRetention =>
        KeepLast > 0 || KeepHourly > 0 || KeepDaily > 0 ||
        KeepWeekly > 0 || KeepMonthly > 0 || KeepYearly > 0;
}
