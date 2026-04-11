using System;
using System.Globalization;
using Avalonia.Data.Converters;
using BorgMate.Localization;

namespace BorgMate.Views.Converters;

public class FormatBytesConverter : IValueConverter
{
    public static readonly FormatBytesConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is long bytes ? Strings.FormatBytes(bytes) : null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
