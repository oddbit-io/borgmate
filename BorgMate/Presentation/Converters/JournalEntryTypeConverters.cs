using System;
using System.Globalization;
using Avalonia.Data.Converters;
using BorgMate.Domain.Journal;

namespace BorgMate.Presentation.Converters;

public class JournalEntryTypeToIconConverter : IValueConverter
{
    public static readonly JournalEntryTypeToIconConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is JournalResult r
            ? r switch
            {
                JournalResult.Completed => "CheckCircleFill",
                JournalResult.Failed => "XCircleFill",
                JournalResult.Cancelled => "DashCircleFill",
                JournalResult.Running => "Clock",
                _ => "Clock"
            }
            : "Clock";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}