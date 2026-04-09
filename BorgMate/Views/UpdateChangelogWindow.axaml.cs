using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Media;
using BorgMate.Localization;

namespace BorgMate.Views;

public partial class UpdateChangelogWindow : ModalWindow
{
    public bool Confirmed { get; private set; }

    public UpdateChangelogWindow()
    {
        InitializeComponent();
    }

    public UpdateChangelogWindow(string version, List<UpdateService.ChangelogEntry> changelog) : this()
    {
        Title = Strings.Get("UpdateTitle");
        TitleText.Text = string.Format(Strings.Get("UpdateChangelogTitle"), version);

        if (changelog.Count > 0)
        {
            foreach (var entry in changelog)
            {
                var section = new StackPanel { Spacing = 4 };
                section.Children.Add(new TextBlock
                {
                    Text = $"v{entry.Version}" + (entry.Date is not null ? $" \u2014 {entry.Date}" : ""),
                    FontWeight = FontWeight.SemiBold,
                    Classes = { "secondary" }
                });
                foreach (var change in entry.Changes)
                    section.Children.Add(new TextBlock
                    {
                        Text = $"  \u2022 {change}",
                        TextWrapping = TextWrapping.Wrap
                    });
                ChangelogPanel.Children.Add(section);
            }
        }
        else
        {
            NoChangelogText.Text = Strings.Get("UpdateNoChangelog");
            NoChangelogText.IsVisible = true;
        }

        DownloadButton.Click += (_, _) => { Confirmed = true; Close(); };
        CancelButton.Click += (_, _) => Close();
    }
}
