using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BorgMate.Services.Config;

public partial class StatusService : ObservableObject, IStatusService
{
    [ObservableProperty]
    private string? _updateMessage;

    [ObservableProperty]
    private int _updateProgress;

    [ObservableProperty]
    private bool _isDownloading;

    private Func<Task>? _updateAction;

    public async void SetError(string message)
    {
        try { await DialogHelper.ErrorAsync(message); }
        catch (Exception ex) { Console.Error.WriteLine($"Failed to show error dialog: {ex.Message}"); }
    }

    public async void SetError(string message, string repoName, string repoPath)
    {
        try { await DialogHelper.ErrorAsync(message, repoName, repoPath); }
        catch (Exception ex) { Console.Error.WriteLine($"Failed to show error dialog: {ex.Message}"); }
    }

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
