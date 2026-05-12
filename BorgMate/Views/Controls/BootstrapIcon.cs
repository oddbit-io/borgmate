using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Platform;

namespace BorgMate.Views.Controls;

public partial class BootstrapIcon : Control
{
    public static readonly StyledProperty<string> KindProperty =
        AvaloniaProperty.Register<BootstrapIcon, string>(nameof(Kind), "");

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner<BootstrapIcon>();

    public string Kind
    {
        get => GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    static BootstrapIcon()
    {
        AffectsRender<BootstrapIcon>(KindProperty, ForegroundProperty);
    }

    public override void Render(DrawingContext context)
    {
        if (string.IsNullOrEmpty(Kind) || !Icons.TryGetValue(Kind, out var path))
            return;

        var scale = new Matrix(Bounds.Width / 16, 0, 0, Bounds.Height / 16, 0, 0);

        using (context.PushTransform(scale))
        {
            var brush = Foreground ?? Brushes.Black;
            context.DrawGeometry(brush, null, path);
        }
    }

    private static readonly FrozenDictionary<string, Geometry> Icons = LoadIcons();

    [GeneratedRegex("""d="([^"]+)""")]
    private static partial Regex PathDataRegex();

    private static FrozenDictionary<string, Geometry> LoadIcons()
    {
        var icons = new Dictionary<string, Geometry>();
        var regex = PathDataRegex();

        var names = new[]
        {
            "ArrowClockwise", "ArrowUpCircle", "ArrowsCollapse", "Bell", "Box", "BoxArrowDown",
            "CheckCircleFill", "ChevronDown", "ChevronRight", "Clock", "DashCircleFill",
            "Database", "ExclamationCircle", "FileDiff", "FolderFill", "FolderPlus",
            "GearWideConnected", "HourglassSplit", "InfoCircle", "KeyFill", "List", "ListNested",
            "PencilSquare", "PlayFill", "PlusLg", "QuestionCircle", "Scissors", "ShieldCheck",
            "StopFill", "ThreeDotsVertical", "Trash", "XCircleFill",
            "FileEarmark", "FileEarmarkCode", "FileEarmarkText", "FileEarmarkPdf",
            "FileEarmarkImage", "FileEarmarkMusic", "FileEarmarkPlay", "FileEarmarkZip",
            "FileEarmarkWord", "FileEarmarkRichtext", "FileEarmarkExcel", "FileEarmarkPpt",
            "FileEarmarkFont", "FileEarmarkBinary", "FileEarmarkSpreadsheet",
            "Files", "Folder2Open",
        };

        foreach (var name in names)
        {
            try
            {
                var uri = new Uri($"avares://BorgMate/Assets/Icons/{name}.svg");
                using var stream = AssetLoader.Open(uri);
                using var reader = new StreamReader(stream);
                var svg = reader.ReadToEnd();

                var matches = regex.Matches(svg);
                if (matches.Count == 0) continue;

                if (matches.Count == 1)
                {
                    icons[name] = Geometry.Parse(matches[0].Groups[1].Value);
                }
                else
                {
                    var group = new GeometryGroup();
                    foreach (Match m in matches)
                        group.Children.Add(Geometry.Parse(m.Groups[1].Value));
                    icons[name] = group;
                }
            }
            catch
            {
                // Skip icons that fail to load
            }
        }

        return icons.ToFrozenDictionary();
    }
}
