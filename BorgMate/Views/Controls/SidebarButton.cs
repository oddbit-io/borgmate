using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using BorgMate.Models;

namespace BorgMate.Views.Controls;

public class SidebarButton : Button
{
    public static readonly StyledProperty<string> IconProperty =
        AvaloniaProperty.Register<SidebarButton, string>(nameof(Icon), "");

    public static readonly StyledProperty<string?> LabelProperty =
        AvaloniaProperty.Register<SidebarButton, string?>(nameof(Label));

    public static readonly StyledProperty<bool> IsExpandedProperty =
        AvaloniaProperty.Register<SidebarButton, bool>(nameof(IsExpanded));

    public static readonly StyledProperty<AppPage?> PageProperty =
        AvaloniaProperty.Register<SidebarButton, AppPage?>(nameof(Page));

    public static readonly StyledProperty<AppPage> ActivePageProperty =
        AvaloniaProperty.Register<SidebarButton, AppPage>(nameof(ActivePage));

    public static readonly StyledProperty<int> BadgeCountProperty =
        AvaloniaProperty.Register<SidebarButton, int>(nameof(BadgeCount));

    public string Icon { get => GetValue(IconProperty); set => SetValue(IconProperty, value); }
    public string? Label { get => GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public bool IsExpanded { get => GetValue(IsExpandedProperty); set => SetValue(IsExpandedProperty, value); }
    public AppPage? Page { get => GetValue(PageProperty); set => SetValue(PageProperty, value); }
    public AppPage ActivePage { get => GetValue(ActivePageProperty); set => SetValue(ActivePageProperty, value); }
    public int BadgeCount { get => GetValue(BadgeCountProperty); set => SetValue(BadgeCountProperty, value); }

    private Border? _badge;
    private TextBlock? _badgeText;

    static SidebarButton()
    {
        PageProperty.Changed.AddClassHandler<SidebarButton>((btn, _) => btn.UpdateActivePage());
        ActivePageProperty.Changed.AddClassHandler<SidebarButton>((btn, _) => btn.UpdateActivePage());
        BadgeCountProperty.Changed.AddClassHandler<SidebarButton>((btn, _) => btn.UpdateBadge());
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _badge = e.NameScope.Find<Border>("PART_Badge");
        _badgeText = e.NameScope.Find<TextBlock>("PART_BadgeText");
        UpdateBadge();
    }

    private void UpdateActivePage()
    {
        PseudoClasses.Set(":active-page", Page is not null && Page == ActivePage);
    }

    private void UpdateBadge()
    {
        if (_badge is not null) _badge.IsVisible = BadgeCount > 0;
        if (_badgeText is not null) _badgeText.Text = BadgeCount.ToString();
    }
}
