using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using BorgMate.Domain.Archives;

namespace BorgMate.Presentation.Converters;

public class FileChangeForegroundConverter : IValueConverter
{
    public static readonly FileChangeForegroundConverter Instance = new();
    private static readonly IBrush AccentBrush = ConverterColors.AccentBrush;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is FileChangeKind.Added ? AccentBrush : AvaloniaProperty.UnsetValue;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}