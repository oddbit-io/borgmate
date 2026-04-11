using System;
using System.Globalization;
using Avalonia.Data.Converters;
using BorgMate.Localization;
using BorgMate.Models;

namespace BorgMate.Views.Converters;

/// <summary>Converts a JournalResult enum to a localized display string.</summary>
public class JournalResultConverter : IValueConverter
{
    public static readonly JournalResultConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is JournalResult r ? Strings.FormatJournalResult(r) : null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
