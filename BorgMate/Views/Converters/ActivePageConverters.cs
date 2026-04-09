using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Styling;
using BorgMate.Models;

namespace BorgMate.Views.Converters;

public class ActivePageToBrushConverter : IMultiValueConverter
{
    public static ActivePageToBrushConverter Instance { get; } = new();

    private static readonly IBrush ActiveBrush = new SolidColorBrush(Color.Parse("#2857B8"));
    private static readonly IBrush InactiveBrush = Brushes.Transparent;

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count >= 1 && values[0] is AppPage current && parameter is AppPage target)
            return current == target ? ActiveBrush : InactiveBrush;
        return InactiveBrush;
    }
}

public class ActivePageToForegroundConverter : IMultiValueConverter
{
    public static ActivePageToForegroundConverter Instance { get; } = new();

    private static readonly IBrush LightDefault = new SolidColorBrush(Color.Parse("#212121"));
    private static readonly IBrush DarkDefault = new SolidColorBrush(Color.Parse("#E0E0E0"));

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        var isActive = values.Count >= 1 && values[0] is AppPage current
                       && parameter is AppPage target && current == target;
        if (isActive)
            return Brushes.White;

        var isDark = values.Count >= 2 && values[1] is ThemeVariant variant && variant == ThemeVariant.Dark;
        return isDark ? DarkDefault : LightDefault;
    }
}
