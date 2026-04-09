using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Input;

namespace BorgMate.Views;

public partial class AboutWindow : ModalWindow
{
    public AboutWindow()
    {
        InitializeComponent();
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = $"Version {version?.Major}.{version?.Minor}.{version?.Build}";
        WebsiteLink.Click += (_, _) => OpenUrl("https://borgmate.oddbit.io");
        StudioLink.Click += (_, _) => OpenUrl("https://oddbit.io/");
        LicenseLink.Click += async (_, _) => await new LicenseWindow().ShowDialog(this);
        ThirdPartyLink.Click += async (_, _) => await new ThirdPartyNoticesWindow().ShowDialog(this);
        ContactLink.Click += (_, _) => OpenUrl("mailto:contact@oddbit.io");
        KeyDown += (_, e) =>
        {
            if (e.Key is Key.Escape or Key.Enter) { Close(); e.Handled = true; }
        };
    }

    private static void OpenUrl(string url)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            Process.Start("open", url)?.Dispose();
        else
            Process.Start("xdg-open", url)?.Dispose();
    }
}
