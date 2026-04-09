using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Avalonia.Data.Converters;

namespace BorgMate.Views.Converters;

public class PathToBreadcrumbConverter : IValueConverter
{
    public static readonly PathToBreadcrumbConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrEmpty(path))
            return Array.Empty<PathSegment>();

        var parts = path.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
        var segments = new List<PathSegment>();

        for (var i = 0; i < parts.Length; i++)
            segments.Add(new PathSegment(parts[i], i < parts.Length - 1));

        return segments;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public record PathSegment(string Name, bool HasSeparator);
