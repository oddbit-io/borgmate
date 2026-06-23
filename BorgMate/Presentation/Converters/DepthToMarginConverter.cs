using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;

namespace BorgMate.Presentation.Converters;

public class DepthToMarginConverter : IValueConverter
{
    private const double IndentPerLevel = 20;
    public static readonly DepthToMarginConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int depth)
            return new Thickness(depth * IndentPerLevel, 0, 0, 0);
        return new Thickness(0);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}