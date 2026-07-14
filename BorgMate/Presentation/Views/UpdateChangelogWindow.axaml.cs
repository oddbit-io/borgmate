using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Media;
using BorgMate.Infrastructure.Updates;

namespace BorgMate.Presentation.Views;

public partial class UpdateChangelogWindow : ModalWindow
{
    public UpdateChangelogWindow()
    {
        InitializeComponent();
    }

    public UpdateChangelogWindow(string version, List<ChangelogEntry> changelog) : this()
    {
        Title = Strings.Get("UpdateTitle");
        TitleText.Text = string.Format(Strings.Get("UpdateChangelogTitle"), version);

        if (changelog.Count > 0)
        {
            foreach (var entry in changelog)
            {
                var section = new StackPanel { Spacing = 8 };
                section.Children.Add(new TextBlock
                {
                    Text = $"v{entry.Version}" + (entry.Date is not null ? $" \u2014 {entry.Date}" : ""),
                    FontWeight = FontWeight.SemiBold,
                    Classes = { "secondary" }
                });
                AddCategory(section, Strings.Get("UpdateChangelogFeatures"), entry.Changes.Features);
                AddCategory(section, Strings.Get("UpdateChangelogImprovements"), entry.Changes.Improvements);
                AddCategory(section, Strings.Get("UpdateChangelogFixes"), entry.Changes.Fixes);
                AddCategory(section, Strings.Get("UpdateChangelogSecurity"), entry.Changes.Security);
                ChangelogPanel.Children.Add(section);
            }
        }
        else
        {
            NoChangelogText.Text = Strings.Get("UpdateNoChangelog");
            NoChangelogText.IsVisible = true;
        }

        DownloadButton.Click += (_, _) =>
        {
            Confirmed = true;
            Close();
        };
        CancelButton.Click += (_, _) => Close();
    }

    public bool Confirmed { get; private set; }

    private static void AddCategory(StackPanel section, string title, List<string> changes)
    {
        if (changes.Count == 0) return;

        var category = new StackPanel { Spacing = 2 };
        category.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = FontWeight.SemiBold,
            Classes = { "secondary" }
        });
        foreach (var change in changes)
            category.Children.Add(new TextBlock
            {
                Text = $"  \u2022 {change}",
                TextWrapping = TextWrapping.Wrap
            });
        section.Children.Add(category);
    }
}
