using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace BorgMate.Views;

public partial class NewPassphraseDialogWindow : ModalWindow
{
    public string? Password { get; private set; }
    public bool SaveToKeychain { get; private set; }

    public NewPassphraseDialogWindow()
    {
        InitializeComponent();
    }

    public NewPassphraseDialogWindow(string title, string message) : this()
    {
        Title = title;
        MessageText.Text = message;

        NewPasswordBox.TextChanged += (_, _) => UpdateValidity();
        ConfirmPasswordBox.TextChanged += (_, _) => UpdateValidity();

        OkButton.Click += (_, _) =>
        {
            Password = NewPasswordBox.Text;
            NewPasswordBox.Text = string.Empty;
            ConfirmPasswordBox.Text = string.Empty;
            SaveToKeychain = SaveCheck.IsChecked == true;
            Close();
        };
        CancelButton.Click += (_, _) => Close();

        Opened += (_, _) => NewPasswordBox.Focus();
    }

    private void UpdateValidity()
    {
        var newText = NewPasswordBox.Text ?? string.Empty;
        var confirmText = ConfirmPasswordBox.Text ?? string.Empty;
        var bothFilled = newText.Length > 0 && confirmText.Length > 0;
        var match = string.Equals(newText, confirmText, StringComparison.Ordinal);
        OkButton.IsEnabled = bothFilled && match;
        MismatchText.Opacity = bothFilled && !match ? 1 : 0;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Enter && OkButton.IsEnabled)
        {
            OkButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            e.Handled = true;
            return;
        }
        base.OnKeyDown(e);
    }
}
