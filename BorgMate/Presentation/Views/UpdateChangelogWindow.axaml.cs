using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using BorgMate.Infrastructure.Updates;
using BorgMate.Presentation.Controls;

namespace BorgMate.Presentation.Views;

public partial class UpdateChangelogWindow : ModalWindow
{
    private const string ReleaseDateFormat = "d MMMM yyyy";

    public UpdateChangelogWindow()
    {
        InitializeComponent();
    }

    public UpdateChangelogWindow(string version, List<ChangelogEntry> changelog) : this()
    {
        Title = Strings.Get("UpdateTitle");

        if (changelog.Count > 0)
        {
            foreach (var entry in changelog)
                ChangelogPanel.Children.Add(CreateEntrySection(entry));
        }
        else
        {
            var section = new StackPanel { Spacing = 12 };
            section.Children.Add(CreateVersionHeader(version, null));
            section.Children.Add(new TextBlock
            {
                Text = Strings.Get("UpdateNoChangelog"),
                Classes = { "secondary" }
            });
            ChangelogPanel.Children.Add(section);
        }

        DownloadButton.Click += (_, _) =>
        {
            Confirmed = true;
            Close();
        };
        CancelButton.Click += (_, _) => Close();
    }

    public bool Confirmed { get; private set; }

    private static StackPanel CreateEntrySection(ChangelogEntry entry)
    {
        var section = new StackPanel { Spacing = 16 };
        section.Children.Add(CreateVersionHeader(entry.Version, entry.Date));
        AddCategory(section, Strings.Get("UpdateChangelogFeatures"), "PlusCircleFill", entry.Changes.Features);
        AddCategory(section, Strings.Get("UpdateChangelogImprovements"), "ArrowUpCircleFill",
            entry.Changes.Improvements);
        AddCategory(section, Strings.Get("UpdateChangelogFixes"), "CheckCircleFill", entry.Changes.Fixes);
        AddCategory(section, Strings.Get("UpdateChangelogSecurity"), "ShieldCheck", entry.Changes.Security);
        return section;
    }

    private static DockPanel CreateVersionHeader(string version, DateOnly? date)
    {
        var header = new DockPanel();
        if (date is { } releaseDate)
        {
            var dateText = new TextBlock
            {
                Text = releaseDate.ToString(ReleaseDateFormat, Strings.Culture),
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(8, 0, 0, 2),
                Classes = { "secondary" }
            };
            DockPanel.SetDock(dateText, Dock.Right);
            header.Children.Add(dateText);
        }

        header.Children.Add(new TextBlock { Text = $"v{version}", Classes = { "changelog-version" } });
        return header;
    }

    private static void AddCategory(StackPanel section, string title, string iconKind, List<string> changes)
    {
        if (changes.Count == 0) return;

        var category = new StackPanel { Spacing = 10 };
        category.Children.Add(new TextBlock
        {
            Text = title.ToUpper(CultureInfo.CurrentUICulture),
            Classes = { "changelog-category" }
        });
        foreach (var change in changes)
        {
            var row = new DockPanel();
            var icon = new BootstrapIcon { Kind = iconKind, Classes = { "changelog-bullet" } };
            DockPanel.SetDock(icon, Dock.Left);
            row.Children.Add(icon);
            row.Children.Add(new TextBlock { Text = change, TextWrapping = TextWrapping.Wrap });
            category.Children.Add(row);
        }

        section.Children.Add(category);
    }
}
