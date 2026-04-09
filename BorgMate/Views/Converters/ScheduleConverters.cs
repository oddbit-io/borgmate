using System;
using System.Globalization;
using Avalonia.Data.Converters;
using BorgMate.Localization;
using BorgMate.Models;

namespace BorgMate.Views.Converters;

public class FrequencyToStringConverter : IValueConverter
{
    public static FrequencyToStringConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is ScheduleFrequency freq ? Strings.Get($"Freq.{freq}") : value?.ToString();

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public class DayOfWeekToStringConverter : IValueConverter
{
    public static DayOfWeekToStringConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is DayOfWeek dow ? Strings.Get($"Dow.{dow}") : value?.ToString();

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
