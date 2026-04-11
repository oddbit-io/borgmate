using System;
using System.Globalization;
using Avalonia.Data.Converters;
using BorgMate.Localization;

namespace BorgMate.Views.Converters;

public class FormatNumberConverter : IValueConverter
{
    public static readonly FormatNumberConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is long n ? n.ToString("N0", Strings.Culture) : null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
