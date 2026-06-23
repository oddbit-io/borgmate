using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using BorgMate.Domain.Settings;

namespace BorgMate.Presentation.Views;

public partial class RestoreOptionsWindow : ModalWindow
{
    public RestoreOptionsWindow()
    {
        InitializeComponent();
    }

    public RestoreOptionsWindow(BorgExtractOptions defaults) : this()
    {
        NoFlagsCheck.IsChecked = defaults.NoFlags;
        NoAclsCheck.IsChecked = defaults.NoAcls;
        NoXattrsCheck.IsChecked = defaults.NoXattrs;
        SparseCheck.IsChecked = defaults.Sparse;

        RestoreButton.Click += (_, _) =>
        {
            ExtractOptions = new BorgExtractOptions
            {
                NoFlags = NoFlagsCheck.IsChecked == true,
                NoAcls = NoAclsCheck.IsChecked == true,
                NoXattrs = NoXattrsCheck.IsChecked == true,
                Sparse = SparseCheck.IsChecked == true
            };
            SaveAsDefault = SaveAsDefaultCheck.IsChecked == true;
            Close();
        };
        CancelButton.Click += (_, _) => Close();
    }

    public BorgExtractOptions? ExtractOptions { get; private set; }
    public bool SaveAsDefault { get; private set; }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            RestoreButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }
}
