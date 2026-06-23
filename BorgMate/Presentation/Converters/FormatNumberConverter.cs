using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace BorgMate.Presentation.Converters;

public class FormatNumberConverter : IValueConverter
{
    public static readonly FormatNumberConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is long n ? n.ToString("N0", Strings.Culture) : null;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}