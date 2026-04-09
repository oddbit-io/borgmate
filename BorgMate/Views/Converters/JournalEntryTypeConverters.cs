using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using BorgMate.Models;

namespace BorgMate.Views.Converters;

public class JournalEntryTypeToIconConverter : IValueConverter
{
    public static readonly JournalEntryTypeToIconConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is JournalResult r ? r switch
        {
            JournalResult.Completed => "CheckCircleFill",
            JournalResult.Failed => "XCircleFill",
            JournalResult.Cancelled => "DashCircleFill",
            JournalResult.Running => "Clock",
            _ => "Clock"
        } : "Clock";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public class JournalEntryTypeToColorConverter : IValueConverter
{
    public static readonly JournalEntryTypeToColorConverter Instance = new();

    private static readonly ISolidColorBrush Green = new SolidColorBrush(Color.Parse("#34C759"));
    private static readonly ISolidColorBrush Red = new SolidColorBrush(Color.Parse("#FF3B30"));
    private static readonly ISolidColorBrush Blue = new SolidColorBrush(Color.Parse("#3C81F7"));
    private static readonly ISolidColorBrush Gray = new SolidColorBrush(Color.Parse("#8E8E93"));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is JournalResult r ? r switch
        {
            JournalResult.Completed => Green,
            JournalResult.Failed => Red,
            JournalResult.Cancelled => Gray,
            JournalResult.Running => Blue,
            _ => Blue
        } : Blue;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
