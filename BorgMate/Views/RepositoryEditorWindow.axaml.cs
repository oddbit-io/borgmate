using Avalonia.Controls;

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
}
