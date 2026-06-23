using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace BorgMate.Presentation.Converters;

public class FormatBytesConverter : IValueConverter
{
    public static readonly FormatBytesConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is long bytes ? Strings.FormatBytes(bytes) : null;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}