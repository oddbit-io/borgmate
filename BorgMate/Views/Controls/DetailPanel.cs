using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace BorgMate.Views.Controls;

/// <summary>
/// Shared chrome for the Repositories page bottom-docked detail panels.
/// </summary>
public class DetailPanel : ContentControl
{
    public static readonly StyledProperty<string> PlaceholderTextProperty =
        AvaloniaProperty.Register<DetailPanel, string>(nameof(PlaceholderText), string.Empty);

    public static readonly StyledProperty<bool> ShowPlaceholderProperty =
        AvaloniaProperty.Register<DetailPanel, bool>(nameof(ShowPlaceholder));

    public static readonly StyledProperty<string> IconKindProperty =
        AvaloniaProperty.Register<DetailPanel, string>(nameof(IconKind), string.Empty);

    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<DetailPanel, string?>(nameof(Title));

    public static readonly StyledProperty<string?> SubtitleProperty =
        AvaloniaProperty.Register<DetailPanel, string?>(nameof(Subtitle));

    public static readonly StyledProperty<bool> IsLoadingProperty =
        AvaloniaProperty.Register<DetailPanel, bool>(nameof(IsLoading));

    public static readonly StyledProperty<bool> ShowRefreshButtonProperty =
        AvaloniaProperty.Register<DetailPanel, bool>(nameof(ShowRefreshButton));

    public static readonly StyledProperty<ICommand?> RefreshCommandProperty =
        AvaloniaProperty.Register<DetailPanel, ICommand?>(nameof(RefreshCommand));

    public string PlaceholderText { get => GetValue(PlaceholderTextProperty); set => SetValue(PlaceholderTextProperty, value); }
    public bool ShowPlaceholder { get => GetValue(ShowPlaceholderProperty); set => SetValue(ShowPlaceholderProperty, value); }
    public string IconKind { get => GetValue(IconKindProperty); set => SetValue(IconKindProperty, value); }
    public string? Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string? Subtitle { get => GetValue(SubtitleProperty); set => SetValue(SubtitleProperty, value); }
    public bool IsLoading { get => GetValue(IsLoadingProperty); set => SetValue(IsLoadingProperty, value); }
    public bool ShowRefreshButton { get => GetValue(ShowRefreshButtonProperty); set => SetValue(ShowRefreshButtonProperty, value); }
    public ICommand? RefreshCommand { get => GetValue(RefreshCommandProperty); set => SetValue(RefreshCommandProperty, value); }
}
