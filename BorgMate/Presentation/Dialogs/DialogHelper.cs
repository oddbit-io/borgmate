using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using BorgMate.Domain.Repositories;
using BorgMate.Presentation.Views;

namespace BorgMate.Presentation.Dialogs;

public static class DialogHelper
{
    public static Window? GetMainWindow()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            return desktop.MainWindow;
        return null;
    }

    /// <summary>
    ///     Returns the innermost visible window in the main window's ownership chain, such as the open
    ///     repository editor. A dialog shown over it blocks every window beneath, including that editor.
    /// </summary>
    public static Window? GetDialogOwner()
    {
        var owner = GetMainWindow();
        while (owner?.OwnedWindows.LastOrDefault(window => window.IsVisible) is { } owned)
            owner = owned;
        return owner;
    }

    public static async Task<bool> ConfirmAsync(string message, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var window = GetDialogOwner();
        if (window is null) return false;

        var dialog = new ConfirmDialogWindow(message);
        using var cancellation = ct.Register(() => Dispatcher.UIThread.Post(() => dialog.Close()));
        await dialog.ShowDialog(window);
        ct.ThrowIfCancellationRequested();
        return dialog.Confirmed;
    }

    /// <summary>Shows Borg's relocated-repository warning and returns true when the user confirms the new location.</summary>
    public static Task<bool> ConfirmRelocationAsync(string repositoryName, RepositoryRelocation relocation,
        CancellationToken ct = default)
    {
        return ConfirmAsync(string.Format(
            Strings.Get("RepositoryRelocated.Confirm"),
            repositoryName,
            relocation.Location,
            relocation.PreviousLocation), ct);
    }

    /// <summary>
    ///     Shows a password prompt dialog with a "Save to Keychain" checkbox.
    ///     Used by both PromptingPassphraseProvider and SshAgentHelper.
    /// </summary>
    public static async Task<(string? password, bool saveToKeychain)> ShowPasswordDialogAsync(
        Window parent, string title, string message, string placeholder)
    {
        var dialog = new PasswordDialogWindow(title, message, placeholder);
        await dialog.ShowDialog(parent);
        return (string.IsNullOrWhiteSpace(dialog.Password) ? null : dialog.Password, dialog.SaveToKeychain);
    }

    /// <summary>
    ///     Prompts for a new passphrase with confirmation. OK stays disabled until both
    ///     fields are non-empty and match. Returns (null, _) on cancel or empty submit.
    /// </summary>
    public static async Task<(string? passphrase, bool saveToKeychain)> ShowNewPassphraseDialogAsync(
        Window parent, string title, string message)
    {
        var dialog = new NewPassphraseDialogWindow(title, message);
        await dialog.ShowDialog(parent);
        return (string.IsNullOrWhiteSpace(dialog.Password) ? null : dialog.Password, dialog.SaveToKeychain);
    }
}
