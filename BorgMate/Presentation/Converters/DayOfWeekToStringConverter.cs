using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace BorgMate.Presentation.Converters;

public class DayOfWeekToStringConverter : IValueConverter
{
    public static DayOfWeekToStringConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is DayOfWeek dow ? Strings.Get($"Dow.{dow}") : value?.ToString();
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}