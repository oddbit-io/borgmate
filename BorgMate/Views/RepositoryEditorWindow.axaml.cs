using Avalonia.Controls;
using BorgMate.ViewModels;

namespace BorgMate.Views;

public partial class RepositoryEditorWindow : ModalWindow
{
    public RepositoryEditorWindow()
    {
        InitializeComponent();
        if (Design.IsDesignMode) return;
        CancelButton.Click += (_, _) => Close();
        Opened += (_, _) => NameInput.Focus();
    }

    /// <summary>
    /// Blocks every close path (Escape, system close, Alt+F4, explicit Close())
    /// while borg verification is in flight and has not yet succeeded. The
    /// IsSaved check is load-bearing: the VM sets IsSaved=true synchronously
    /// inside Save's try-block (before the finally clears IsVerifying), which
    /// triggers ModalWindow's auto-close. Without the IsSaved exception here,
    /// the successful-close path would be cancelled and the dialog would hang
    /// open even after verification passed.
    /// </summary>
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (DataContext is RepositoryEditorViewModel { IsVerifying: true, IsSaved: false })
            e.Cancel = true;
        base.OnClosing(e);
    }
}
