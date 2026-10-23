using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using BorgMate.Domain.Journal;

namespace BorgMate.Presentation.Converters;

public class JournalEntryTypeToColorConverter : IValueConverter
{
    public static readonly JournalEntryTypeToColorConverter Instance = new();

    private static readonly ISolidColorBrush Green = new SolidColorBrush(Color.Parse("#34C759"));
    private static readonly ISolidColorBrush Red = new SolidColorBrush(Color.Parse("#FF3B30"));
    private static readonly ISolidColorBrush Blue = ConverterColors.AccentBrush;
    private static readonly ISolidColorBrush Gray = new SolidColorBrush(Color.Parse("#8E8E93"));
    private static readonly ISolidColorBrush Orange = new SolidColorBrush(Color.Parse("#FF9500"));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is JournalResult r
            ? r switch
            {
                JournalResult.Completed => Green,
                JournalResult.Failed => Red,
                JournalResult.Cancelled => Gray,
                JournalResult.Deferred => Orange,
                JournalResult.CompletedWithWarnings => Orange,
                JournalResult.Running => Blue,
                _ => Blue
            }
            : Blue;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}