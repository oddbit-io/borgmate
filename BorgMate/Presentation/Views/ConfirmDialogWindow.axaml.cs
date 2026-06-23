using Avalonia.Input;

namespace BorgMate.Presentation.Views;

public partial class ConfirmDialogWindow : ModalWindow
{
    public ConfirmDialogWindow()
    {
        InitializeComponent();
    }

    public ConfirmDialogWindow(string message) : this()
    {
        MessageText.Text = message;
        OkButton.Click += (_, _) =>
        {
            Confirmed = true;
            Close();
        };
        CancelButton.Click += (_, _) => Close();
    }

    public bool Confirmed { get; private set; }

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