using System;
using System.Globalization;
using Avalonia.Data.Converters;
using BorgMate.Domain.Repositories;
using BorgMate.Domain.Settings;

namespace BorgMate.Presentation.Converters;

public class EnumToBorgStringConverter : IValueConverter
{
    public static EnumToBorgStringConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            BorgEncryptionMode mode => mode.ToBorgString(),
            Borg2EncryptionMode mode => mode.ToBorgString(),
            _ => value?.ToString()
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
