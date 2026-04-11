using System;
using System.Globalization;
using Avalonia.Data.Converters;
using BorgMate.Localization;
using Humanizer;

namespace BorgMate.Views.Converters;

/// <summary>Converts a DateTime to a humanized relative time string ("3 minutes ago").</summary>
public class RelativeTimeConverter : IValueConverter
{
    public static readonly RelativeTimeConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is DateTime dt ? dt.Humanize(culture: Strings.Culture) : null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
