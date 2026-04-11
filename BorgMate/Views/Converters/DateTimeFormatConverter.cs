using System;
using System.Globalization;
using Avalonia.Data.Converters;
using BorgMate.Localization;

namespace BorgMate.Views.Converters;

/// <summary>Formats DateTime with ConverterParameter as format string and Strings.Culture.</summary>
public class DateTimeFormatConverter : IValueConverter
{
    public static readonly DateTimeFormatConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var format = parameter as string ?? "d MMMM yyyy, HH:mm";
        return value switch
        {
            DateTime dt when dt != DateTime.MinValue => dt.ToString(format, Strings.Culture),
            _ => null
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
