using System.Collections.Generic;
using BorgMate.Localization;
using BorgMate.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Humanizer;

namespace BorgMate.ViewModels;

/// <summary>Tracks a running BorgJob for AXAML progress binding.</summary>
public partial class JobProgressTracker : ObservableObject
{
    private readonly bool _confirmCancel;

    /// <param name="confirmCancel">Show confirmation dialog before cancelling.</param>
    public JobProgressTracker(bool confirmCancel = false)
    {
        _confirmCancel = confirmCancel;
    }

    [ObservableProperty]
    private string? _activeStatusMessage;

    [ObservableProperty]
    private string? _activeElapsed;

    [ObservableProperty]
    private double? _activeProgress;

    [ObservableProperty]
    private string? _activeProgressDisplay;

    private string? _activeEta;
    private BorgJob? _activeJob;

    public bool HasActiveJob => _activeJob is not null;

    /// <summary>
    /// Binds to a job for progress tracking. Unsubscribes from the previous job's PropertyChanged
    /// and subscribes to the new one. Pass null to clear the progress display.
    /// </summary>
    public void SetActiveJob(BorgJob? job)
    {
        if (_activeJob == job) return;
        if (_activeJob is not null)
            _activeJob.PropertyChanged -= OnActiveJobPropertyChanged;
        _activeJob = job;
        if (_activeJob is not null)
        {
            _activeJob.PropertyChanged += OnActiveJobPropertyChanged;
            SyncActiveJobProps();
        }
        else
        {
            ActiveStatusMessage = null;
            ActiveElapsed = null;
            ActiveProgress = null;
            ActiveProgressDisplay = null;
            _activeEta = null;
        }
        OnPropertyChanged(nameof(HasActiveJob));
    }

    private void SyncActiveJobProps()
    {
        if (_activeJob is null) return;
        ActiveStatusMessage = _activeJob.StatusMessage;
        ActiveElapsed = _activeJob.ElapsedDisplay;
        ActiveProgress = _activeJob.Progress;
        UpdateEta();
        UpdateProgressDisplay();
    }

    private void UpdateProgressDisplay()
    {
        if (_activeJob?.Progress is { } pct)
        {
            var speed = _activeJob.EstimateSpeed();
            var speedStr = speed is > 0 ? Strings.FormatSpeed(speed.Value) : null;
            var parts = new List<string>();
            if (speedStr is not null) parts.Add(speedStr);
            if (_activeEta is not null) parts.Add(_activeEta);
            ActiveProgressDisplay = parts.Count > 0
                ? $"{pct:0.0}% ({string.Join(", ", parts)})"
                : $"{pct:0.0}%";
        }
        else
            ActiveProgressDisplay = "0.0%";
    }

    private void UpdateEta()
    {
        var remaining = _activeJob?.EstimateRemaining();
        if (remaining is { TotalSeconds: > 0 })
        {
            _activeEta = "~" + remaining.Value.Humanize(precision: 2, minUnit: TimeUnit.Minute, culture: Strings.Culture)
                        + " " + Strings.Get("Job.Left");
        }
        else
        {
            _activeEta = null;
        }
    }

    private void OnActiveJobPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(BorgJob.StatusMessage):
                ActiveStatusMessage = _activeJob?.StatusMessage;
                break;
            case nameof(BorgJob.ElapsedDisplay):
                ActiveElapsed = _activeJob?.ElapsedDisplay;
                break;
            case nameof(BorgJob.Progress):
                ActiveProgress = _activeJob?.Progress;
                UpdateEta();
                UpdateProgressDisplay();
                break;
        }
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task CancelActiveJob()
    {
        if (_activeJob is null) return;
        if (_confirmCancel && !await DialogHelper.ConfirmAsync(Strings.Get("ConfirmCancelOperation")))
            return;
        _activeJob?.Cts.Cancel();
    }
}
