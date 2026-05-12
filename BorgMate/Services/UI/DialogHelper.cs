using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
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
    /// Shows a password prompt dialog with optional "Save to Keychain" checkbox.
    /// Used by both PassphrasePrompt and SshAgentHelper.
    /// </summary>
    public static async Task<(string? password, bool saveToKeychain)> ShowPasswordDialogAsync(
        Window parent, string title, string message, string placeholder)
    {
        string? result = null;
        var saveToKeychain = false;

        var passwordBox = new TextBox { PasswordChar = '*', PlaceholderText = placeholder, Width = 300 };
        var saveCheck = new CheckBox { Content = Strings.Get("SaveToKeychain"), IsChecked = true };
        var okButton = new Button { Content = Strings.Get("OK"), Classes = { "accent" }, Width = 80 };
        var cancelButton = new Button { Content = Strings.Get("Cancel"), Width = 80 };

        var dialog = new Window
        {
            Title = title,
            Width = 380,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(20),
                Spacing = 12,
                Children =
                {
                    new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                    passwordBox,
                    saveCheck,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 8,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Children = { okButton, cancelButton }
                    }
                }
            }
        };

        okButton.Click += (_, _) =>
        {
            result = passwordBox.Text;
            passwordBox.Text = string.Empty;
            saveToKeychain = saveCheck.IsChecked == true;
            dialog.Close();
        };

        cancelButton.Click += (_, _) => dialog.Close();
        dialog.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { okButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); e.Handled = true; }
            else if (e.Key == Key.Escape) { dialog.Close(); e.Handled = true; }
        };
        dialog.Opened += (_, _) => passwordBox.Focus();

        await dialog.ShowDialog(parent);

        return (string.IsNullOrWhiteSpace(result) ? null : result, saveToKeychain);
    }

    /// <summary>
    /// Prompts for a new passphrase with confirmation. OK stays disabled until both
    /// fields are non-empty and match. Returns (null, _) on cancel or empty submit.
    /// </summary>
    public static async Task<(string? passphrase, bool saveToKeychain)> ShowNewPassphraseDialogAsync(
        Window parent, string title, string message)
    {
        string? result = null;
        var saveToKeychain = false;

        var newBox = new TextBox
        {
            PasswordChar = '*',
            PlaceholderText = Strings.Get("ChangePassphrase.NewLabel"),
            Width = 300
        };
        var confirmBox = new TextBox
        {
            PasswordChar = '*',
            PlaceholderText = Strings.Get("ChangePassphrase.ConfirmLabel"),
            Width = 300
        };
        var mismatchText = new TextBlock
        {
            Text = Strings.Get("ChangePassphrase.Mismatch"),
            Foreground = new SolidColorBrush(Color.FromRgb(0xD0, 0x4A, 0x4A)),
            FontSize = 11,
            IsVisible = false
        };
        var saveCheck = new CheckBox { Content = Strings.Get("SaveToKeychain"), IsChecked = true };
        var okButton = new Button { Content = Strings.Get("OK"), Classes = { "accent" }, Width = 80, IsEnabled = false };
        var cancelButton = new Button { Content = Strings.Get("Cancel"), Width = 80 };

        void UpdateValidity()
        {
            var newText = newBox.Text ?? string.Empty;
            var confirmText = confirmBox.Text ?? string.Empty;
            var bothFilled = newText.Length > 0 && confirmText.Length > 0;
            var match = string.Equals(newText, confirmText, StringComparison.Ordinal);
            okButton.IsEnabled = bothFilled && match;
            mismatchText.IsVisible = bothFilled && !match;
        }

        newBox.TextChanged += (_, _) => UpdateValidity();
        confirmBox.TextChanged += (_, _) => UpdateValidity();

        var dialog = new Window
        {
            Title = title,
            Width = 380,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(20),
                Spacing = 12,
                Children =
                {
                    new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                    newBox,
                    confirmBox,
                    mismatchText,
                    saveCheck,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 8,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Children = { okButton, cancelButton }
                    }
                }
            }
        };

        okButton.Click += (_, _) =>
        {
            result = newBox.Text;
            newBox.Text = string.Empty;
            confirmBox.Text = string.Empty;
            saveToKeychain = saveCheck.IsChecked == true;
            dialog.Close();
        };

        cancelButton.Click += (_, _) => dialog.Close();
        dialog.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && okButton.IsEnabled)
            {
                okButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                dialog.Close();
                e.Handled = true;
            }
        };
        dialog.Opened += (_, _) => newBox.Focus();

        await dialog.ShowDialog(parent);

        return (string.IsNullOrWhiteSpace(result) ? null : result, saveToKeychain);
    }
}
