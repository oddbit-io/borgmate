using Avalonia.Input;

namespace BorgMate.Views;

public partial class ErrorDialogWindow : ModalWindow
{
    public ErrorDialogWindow()
    {
        InitializeComponent();
    }

    public ErrorDialogWindow(string message) : this()
    {
        MessageText.Text = message;
        OkButton.Click += (_, _) => Close();
    }

    public ErrorDialogWindow(string message, string repoName, string repoPath) : this()
    {
        MessageText.Text = message;
        RepoNameText.Text = repoName;
        RepoPathText.Text = repoPath;
        RepoHeader.IsVisible = true;
        OkButton.Click += (_, _) => Close();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Close();
            e.Handled = true;
            return;
        }
        base.OnKeyDown(e);
    }
}
