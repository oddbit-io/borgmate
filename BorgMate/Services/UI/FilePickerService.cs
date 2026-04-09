using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

namespace BorgMate.Services.UI;

/// <summary>
/// File picker using Avalonia's StorageProvider. Used on Windows and Linux.
/// </summary>
public class FilePickerService : IFilePickerService
{
    public async Task<string?> PickFolderAsync(string title = "Select Folder")
    {
        var window = GetWindow();
        if (window is null) return null;

        var folders = await window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false
        });

        return folders.Count > 0 ? folders[0].Path.LocalPath : null;
    }

    public async Task<IReadOnlyList<string>> PickFoldersAsync(string title = "Select Folders")
    {
        var window = GetWindow();
        if (window is null) return [];

        var folders = await window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = true
        });

        return folders.Select(f => f.Path.LocalPath).ToList();
    }

    public async Task<string?> PickFileAsync(string title = "Select File")
    {
        var window = GetWindow();
        if (window is null) return null;

        var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false
        });

        return files.Count > 0 ? files[0].Path.LocalPath : null;
    }

    private static Window? GetWindow()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            return desktop.MainWindow;
        return null;
    }
}
