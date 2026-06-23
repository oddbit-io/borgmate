using System;
using System.Globalization;
using Avalonia.Data.Converters;
using BorgMate.Domain.Repositories;

namespace BorgMate.Presentation.Converters;

public class FrequencyToStringConverter : IValueConverter
{
    public static FrequencyToStringConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is ScheduleFrequency freq ? Strings.Get($"Freq.{freq}") : value?.ToString();
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}