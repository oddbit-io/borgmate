using Avalonia.Controls;

namespace BorgMate.Views;

public partial class AppSettingsWindow : ModalWindow
{
    public AppSettingsWindow()
    {
        InitializeComponent();
        if (Design.IsDesignMode) return;
        CloseButton.Click += (_, _) => Close();
    }
}
