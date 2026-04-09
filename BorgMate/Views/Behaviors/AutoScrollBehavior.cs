using System.Collections.Specialized;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace BorgMate.Views.Behaviors;

public class AutoScrollBehavior : AvaloniaObject
{
    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<AutoScrollBehavior, ScrollViewer, bool>("Enabled");

    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<AutoScrollBehavior, ListBox, bool>("IsEnabled");

    public static bool GetEnabled(ScrollViewer obj) => obj.GetValue(EnabledProperty);
    public static void SetEnabled(ScrollViewer obj, bool value) => obj.SetValue(EnabledProperty, value);

    public static bool GetIsEnabled(ListBox obj) => obj.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(ListBox obj, bool value) => obj.SetValue(IsEnabledProperty, value);

    static AutoScrollBehavior()
    {
        EnabledProperty.Changed.AddClassHandler<ScrollViewer>((sv, e) =>
        {
            if (e.NewValue is true)
                sv.ScrollChanged += OnScrollChanged;
            else
                sv.ScrollChanged -= OnScrollChanged;
        });

        IsEnabledProperty.Changed.AddClassHandler<ListBox>((lb, e) =>
        {
            if (e.NewValue is true)
            {
                SubscribeToItemsSource(lb);
                lb.PropertyChanged += OnListBoxPropertyChanged;
            }
            else
            {
                UnsubscribeFromItemsSource(lb);
                lb.PropertyChanged -= OnListBoxPropertyChanged;
            }
        });
    }

    private static readonly ConditionalWeakTable<ListBox, NotifyCollectionChangedEventHandler> _handlers = new();

    private static void OnListBoxPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (sender is ListBox lb && e.Property.Name == nameof(ListBox.ItemsSource))
        {
            UnsubscribeFromItemsSource(lb);
            SubscribeToItemsSource(lb);
        }
    }

    private static void SubscribeToItemsSource(ListBox lb)
    {
        if (lb.ItemsSource is INotifyCollectionChanged ncc)
        {
            NotifyCollectionChangedEventHandler handler = (_, args) =>
            {
                if (args.Action != NotifyCollectionChangedAction.Add) return;
                Dispatcher.UIThread.Post(() =>
                {
                    var sv = lb.FindDescendantOfType<ScrollViewer>();
                    sv?.ScrollToEnd();
                });
            };
            _handlers.AddOrUpdate(lb, handler);
            ncc.CollectionChanged += handler;
        }
    }

    private static void UnsubscribeFromItemsSource(ListBox lb)
    {
        if (_handlers.TryGetValue(lb, out var oldHandler))
        {
            if (lb.ItemsSource is INotifyCollectionChanged oldNcc)
                oldNcc.CollectionChanged -= oldHandler;
            _handlers.Remove(lb);
        }
    }

    private static void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (sender is ScrollViewer sv && e.ExtentDelta.Y > 0)
            sv.ScrollToEnd();
    }
}
