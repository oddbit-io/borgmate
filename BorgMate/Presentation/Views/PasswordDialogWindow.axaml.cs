using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace BorgMate.Presentation.Views;

public partial class PasswordDialogWindow : ModalWindow
{
    public PasswordDialogWindow()
    {
        InitializeComponent();
    }

    public PasswordDialogWindow(string title, string message, string placeholder) : this()
    {
        Title = title;
        MessageText.Text = message;
        PasswordBox.PlaceholderText = placeholder;

        OkButton.Click += (_, _) =>
        {
            Password = PasswordBox.Text;
            PasswordBox.Text = string.Empty;
            SaveToKeychain = SaveCheck.IsChecked == true;
            Close();
        };
        CancelButton.Click += (_, _) => Close();

        Opened += (_, _) => PasswordBox.Focus();
    }

    public string? Password { get; private set; }
    public bool SaveToKeychain { get; private set; }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OkButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }
}