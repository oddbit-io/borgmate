using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using BorgMate.Domain.Archives;

namespace BorgMate.Presentation.Converters;

public class FileChangeFontWeightConverter : IValueConverter
{
    public static readonly FileChangeFontWeightConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is FileChangeKind.Modified ? FontWeight.Bold : FontWeight.Normal;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}