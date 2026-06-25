using System;
using System.Globalization;
using Avalonia.Data.Converters;
using BorgMate.Domain.Archives;

namespace BorgMate.Presentation.Converters;

public class FileChangeAddedConverter : IValueConverter
{
    public static readonly FileChangeAddedConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is FileChangeKind.Added;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
