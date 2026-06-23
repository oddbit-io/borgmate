using System;
using System.Globalization;
using Avalonia.Data.Converters;
using BorgMate.Domain.Settings;

namespace BorgMate.Presentation.Converters;

public class RetentionPeriodConverter : IValueConverter
{
    public static RetentionPeriodConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is RetentionPeriod p ? Strings.Get($"Retention.{p}") : value?.ToString();
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}