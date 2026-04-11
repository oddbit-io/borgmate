using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BorgMate.Services.Config;

public partial class StatusService : ObservableObject
{
    [ObservableProperty]
    private string? _updateMessage;

    [ObservableProperty]
    private int _updateProgress;

    [ObservableProperty]
    private bool _isDownloading;

    private Func<Task>? _updateAction;

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
