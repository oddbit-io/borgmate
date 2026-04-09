using Avalonia;
using Avalonia.Controls;

namespace BorgMate.Views.Controls;

public class ToolbarButton : Button
{
    public static readonly StyledProperty<string> IconProperty =
        AvaloniaProperty.Register<ToolbarButton, string>(nameof(Icon), "");

    public static readonly StyledProperty<string?> LabelProperty =
        AvaloniaProperty.Register<ToolbarButton, string?>(nameof(Label));

    public string Icon { get => GetValue(IconProperty); set => SetValue(IconProperty, value); }
    public string? Label { get => GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
}
