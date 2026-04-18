using System;
using System.Globalization;
using Avalonia.Data.Converters;
using BorgMate.Localization;
using BorgMate.Models;

namespace BorgMate.Views.Converters;

public class LogLevelConverter : IValueConverter
{
    public static LogLevelConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is AppLogLevel level ? Strings.Get($"LogLevel.{level}") : value?.ToString();
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
