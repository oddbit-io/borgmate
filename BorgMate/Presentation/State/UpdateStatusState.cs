using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BorgMate.Presentation.State;

/// <summary>
///     Holds bindable update banner state and the user-triggered update action.
/// </summary>
public partial class UpdateStatusState : ObservableObject
{
    [ObservableProperty]
    private bool _isDownloading;

    private Func<Task>? _updateAction;

    [ObservableProperty]
    private string? _updateMessage;

    [ObservableProperty]
    private int _updateProgress;

    /// <summary>
    ///     Displays an available update message and stores the action to run when the banner is clicked.
    /// </summary>
    public void SetUpdateAvailable(string message, Func<Task> action)
    {
        UpdateMessage = message;
        _updateAction = action;
    }

    [RelayCommand]
    private async Task PerformUpdate()
    {
        if (_updateAction is not null)
            await _updateAction();
    }
}
