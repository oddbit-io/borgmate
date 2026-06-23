using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace BorgMate.Presentation.Converters;

public class ToggleActiveForegroundConverter : IValueConverter
{
    public static readonly ToggleActiveForegroundConverter Instance = new();
    private static readonly IBrush AccentBrush = ConverterColors.AccentBrush;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? AccentBrush : AvaloniaProperty.UnsetValue;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}