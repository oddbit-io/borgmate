using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using BorgMate.Models;

namespace BorgMate.Views.Converters;

public class FileIconConverter : IValueConverter
{
    public static readonly FileIconConverter Instance = new();

    private static readonly FrozenDictionary<string, string> ExtensionIcons =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // Code
            [".cs"] = "FileEarmarkCode", [".js"] = "FileEarmarkCode", [".ts"] = "FileEarmarkCode",
            [".py"] = "FileEarmarkCode", [".rb"] = "FileEarmarkCode", [".go"] = "FileEarmarkCode",
            [".rs"] = "FileEarmarkCode", [".java"] = "FileEarmarkCode", [".cpp"] = "FileEarmarkCode",
            [".c"] = "FileEarmarkCode", [".h"] = "FileEarmarkCode", [".hpp"] = "FileEarmarkCode",
            [".cc"] = "FileEarmarkCode", [".m"] = "FileEarmarkCode", [".mm"] = "FileEarmarkCode",
            [".swift"] = "FileEarmarkCode", [".kt"] = "FileEarmarkCode", [".php"] = "FileEarmarkCode",
            [".sh"] = "FileEarmarkCode", [".bash"] = "FileEarmarkCode", [".zsh"] = "FileEarmarkCode",
            [".ps1"] = "FileEarmarkCode", [".bat"] = "FileEarmarkCode", [".cmd"] = "FileEarmarkCode",
            [".html"] = "FileEarmarkCode", [".htm"] = "FileEarmarkCode",
            [".css"] = "FileEarmarkCode", [".scss"] = "FileEarmarkCode",
            [".less"] = "FileEarmarkCode", [".sass"] = "FileEarmarkCode",
            [".xml"] = "FileEarmarkCode", [".xaml"] = "FileEarmarkCode", [".axaml"] = "FileEarmarkCode",
            [".jsx"] = "FileEarmarkCode", [".tsx"] = "FileEarmarkCode", [".vue"] = "FileEarmarkCode",
            [".svelte"] = "FileEarmarkCode", [".sql"] = "FileEarmarkCode",
            [".lua"] = "FileEarmarkCode", [".dart"] = "FileEarmarkCode", [".scala"] = "FileEarmarkCode",
            [".r"] = "FileEarmarkCode", [".pl"] = "FileEarmarkCode",
            [".proto"] = "FileEarmarkCode", [".graphql"] = "FileEarmarkCode",
            // Text
            [".txt"] = "FileEarmarkText", [".md"] = "FileEarmarkText", [".rst"] = "FileEarmarkText",
            [".log"] = "FileEarmarkText", [".csv"] = "FileEarmarkText",
            [".tex"] = "FileEarmarkText", [".org"] = "FileEarmarkText",
            [".srt"] = "FileEarmarkText", [".vtt"] = "FileEarmarkText",
            // Config
            [".json"] = "FileEarmarkCode", [".yaml"] = "FileEarmarkCode", [".yml"] = "FileEarmarkCode",
            [".toml"] = "FileEarmarkCode", [".ini"] = "FileEarmarkCode", [".cfg"] = "FileEarmarkCode",
            [".conf"] = "FileEarmarkCode", [".env"] = "FileEarmarkCode",
            [".properties"] = "FileEarmarkCode",
            // PDF
            [".pdf"] = "FileEarmarkPdf",
            // Images
            [".jpg"] = "FileEarmarkImage", [".jpeg"] = "FileEarmarkImage", [".png"] = "FileEarmarkImage",
            [".gif"] = "FileEarmarkImage", [".bmp"] = "FileEarmarkImage", [".svg"] = "FileEarmarkImage",
            [".webp"] = "FileEarmarkImage", [".ico"] = "FileEarmarkImage",
            [".tiff"] = "FileEarmarkImage", [".tif"] = "FileEarmarkImage",
            [".psd"] = "FileEarmarkImage", [".raw"] = "FileEarmarkImage",
            [".heic"] = "FileEarmarkImage", [".heif"] = "FileEarmarkImage", [".avif"] = "FileEarmarkImage",
            // Audio
            [".mp3"] = "FileEarmarkMusic", [".wav"] = "FileEarmarkMusic", [".flac"] = "FileEarmarkMusic",
            [".aac"] = "FileEarmarkMusic", [".ogg"] = "FileEarmarkMusic", [".m4a"] = "FileEarmarkMusic",
            [".wma"] = "FileEarmarkMusic", [".opus"] = "FileEarmarkMusic",
            [".aiff"] = "FileEarmarkMusic", [".aif"] = "FileEarmarkMusic",
            [".mid"] = "FileEarmarkMusic", [".midi"] = "FileEarmarkMusic",
            // Video
            [".mp4"] = "FileEarmarkPlay", [".avi"] = "FileEarmarkPlay", [".mkv"] = "FileEarmarkPlay",
            [".mov"] = "FileEarmarkPlay", [".wmv"] = "FileEarmarkPlay", [".flv"] = "FileEarmarkPlay",
            [".webm"] = "FileEarmarkPlay", [".3gp"] = "FileEarmarkPlay",
            [".m4v"] = "FileEarmarkPlay", [".mpg"] = "FileEarmarkPlay", [".mpeg"] = "FileEarmarkPlay",
            [".mts"] = "FileEarmarkPlay", [".vob"] = "FileEarmarkPlay",
            // Archives
            [".zip"] = "FileEarmarkZip", [".tar"] = "FileEarmarkZip", [".gz"] = "FileEarmarkZip",
            [".bz2"] = "FileEarmarkZip", [".xz"] = "FileEarmarkZip", [".7z"] = "FileEarmarkZip",
            [".rar"] = "FileEarmarkZip", [".tgz"] = "FileEarmarkZip", [".zst"] = "FileEarmarkZip",
            [".iso"] = "FileEarmarkZip", [".dmg"] = "FileEarmarkZip",
            [".deb"] = "FileEarmarkZip", [".rpm"] = "FileEarmarkZip",
            [".jar"] = "FileEarmarkZip", [".cab"] = "FileEarmarkZip",
            // Office — Word
            [".doc"] = "FileEarmarkWord", [".docx"] = "FileEarmarkWord", [".odt"] = "FileEarmarkWord",
            [".rtf"] = "FileEarmarkRichtext", [".pages"] = "FileEarmarkWord",
            // Office — Excel
            [".xls"] = "FileEarmarkExcel", [".xlsx"] = "FileEarmarkExcel", [".ods"] = "FileEarmarkExcel",
            [".numbers"] = "FileEarmarkExcel", [".tsv"] = "FileEarmarkExcel",
            // Office — PowerPoint
            [".ppt"] = "FileEarmarkPpt", [".pptx"] = "FileEarmarkPpt", [".odp"] = "FileEarmarkPpt",
            [".key"] = "FileEarmarkPpt",
            // Fonts
            [".ttf"] = "FileEarmarkFont", [".otf"] = "FileEarmarkFont",
            [".woff"] = "FileEarmarkFont", [".woff2"] = "FileEarmarkFont",
            // Binary / executables
            [".exe"] = "FileEarmarkBinary", [".dll"] = "FileEarmarkBinary",
            [".so"] = "FileEarmarkBinary", [".dylib"] = "FileEarmarkBinary",
            [".bin"] = "FileEarmarkBinary", [".o"] = "FileEarmarkBinary",
            [".a"] = "FileEarmarkBinary", [".lib"] = "FileEarmarkBinary",
            [".class"] = "FileEarmarkBinary", [".pyc"] = "FileEarmarkBinary",
            [".msi"] = "FileEarmarkBinary",
            // Database
            [".db"] = "FileEarmarkSpreadsheet", [".sqlite"] = "FileEarmarkSpreadsheet",
            [".sqlite3"] = "FileEarmarkSpreadsheet", [".mdb"] = "FileEarmarkSpreadsheet",
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not ArchiveFileNode node)
            return "FileEarmark";

        if (node.IsDirectory)
            return "FolderFill";

        var name = node.Name;
        var dotIndex = name.LastIndexOf('.');
        if (dotIndex >= 0)
        {
            var ext = name[dotIndex..];
            if (ExtensionIcons.TryGetValue(ext, out var icon))
                return icon;
        }

        return "FileEarmark";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

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

public class ToggleActiveForegroundConverter : IValueConverter
{
    public static readonly ToggleActiveForegroundConverter Instance = new();
    private static readonly IBrush AccentBrush = ConverterColors.AccentBrush;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? AccentBrush : AvaloniaProperty.UnsetValue;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

public class DepthToMarginConverter : IValueConverter
{
    public static readonly DepthToMarginConverter Instance = new();
    private const double IndentPerLevel = 20;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int depth)
            return new Thickness(depth * IndentPerLevel, 0, 0, 0);
        return new Thickness(0);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public static class BrowseArchiveConverters
{
    public static FileIconConverter FileIcon { get; } = FileIconConverter.Instance;
    public static FileChangeFontWeightConverter ChangeFontWeight { get; } = FileChangeFontWeightConverter.Instance;
    public static FileChangeForegroundConverter ChangeForeground { get; } = FileChangeForegroundConverter.Instance;
    public static ToggleActiveForegroundConverter ToggleActiveForeground { get; } = ToggleActiveForegroundConverter.Instance;
    public static DepthToMarginConverter DepthToMargin { get; } = DepthToMarginConverter.Instance;
}
