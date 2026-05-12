using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using BorgMate.Views;

namespace BorgMate.Services.UI;

public static class DialogHelper
{
    public static Window? GetMainWindow()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            return desktop.MainWindow;
        return null;
    }

    public static async Task<bool> ConfirmAsync(string message)
    {
        var window = GetMainWindow();
        if (window is null) return false;

        var dialog = new ConfirmDialogWindow(message);
        await dialog.ShowDialog(window);
        return dialog.Confirmed;
    }

    /// <summary>
    /// Shows a password prompt dialog with a "Save to Keychain" checkbox.
    /// Used by both PassphrasePrompt and SshAgentHelper.
    /// </summary>
    public static async Task<(string? password, bool saveToKeychain)> ShowPasswordDialogAsync(
        Window parent, string title, string message, string placeholder)
    {
        var dialog = new PasswordDialogWindow(title, message, placeholder);
        await dialog.ShowDialog(parent);
        return (string.IsNullOrWhiteSpace(dialog.Password) ? null : dialog.Password, dialog.SaveToKeychain);
    }

    /// <summary>
    /// Prompts for a new passphrase with confirmation. OK stays disabled until both
    /// fields are non-empty and match. Returns (null, _) on cancel or empty submit.
    /// </summary>
    public static async Task<(string? passphrase, bool saveToKeychain)> ShowNewPassphraseDialogAsync(
        Window parent, string title, string message)
    {
        var dialog = new NewPassphraseDialogWindow(title, message);
        await dialog.ShowDialog(parent);
        return (string.IsNullOrWhiteSpace(dialog.Password) ? null : dialog.Password, dialog.SaveToKeychain);
    }
}
