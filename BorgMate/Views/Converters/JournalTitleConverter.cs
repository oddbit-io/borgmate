using System;
using System.Globalization;
using Avalonia.Data.Converters;
using BorgMate.Localization;
using BorgMate.Models;

namespace BorgMate.Views.Converters;

/// <summary>Converts a JournalEntry to its localized title string.</summary>
public class JournalTitleConverter : IValueConverter
{
    public static readonly JournalTitleConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is JournalEntry e ? Strings.FormatJournalTitle(e.EventKind, e.TitleArgs) : null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
