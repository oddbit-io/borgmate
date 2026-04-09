using System;
using System.Globalization;
using Avalonia.Data.Converters;
using BorgMate.Models;

namespace BorgMate.Views.Converters;

public class EnumToBorgStringConverter : IValueConverter
{
    public static EnumToBorgStringConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is BorgEncryptionMode mode ? mode.ToBorgString() : value?.ToString();
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
