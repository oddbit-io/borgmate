using Avalonia.Controls;
using Avalonia.Input;

namespace BorgMate.Views;

public partial class ConfirmDialogWindow : ModalWindow
{
    public bool Confirmed { get; private set; }

    public ConfirmDialogWindow()
    {
        InitializeComponent();
    }

    public ConfirmDialogWindow(string message) : this()
    {
        MessageText.Text = message;
        OkButton.Click += (_, _) => { Confirmed = true; Close(); };
        CancelButton.Click += (_, _) => Close();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Confirmed = true;
            Close();
            e.Handled = true;
            return;
        }
        base.OnKeyDown(e);
    }
}
