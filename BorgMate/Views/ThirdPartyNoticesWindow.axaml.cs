using System;
using System.IO;
using System.Reflection;

namespace BorgMate.Views;

public partial class ThirdPartyNoticesWindow : ModalWindow
{
    public ThirdPartyNoticesWindow()
    {
        InitializeComponent();

        var dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        var path = Path.Combine(dir, "THIRD-PARTY-NOTICES");
        var lines = File.Exists(path) ? File.ReadAllLines(path) : ["File not found."];
        LinesList.ItemsSource = lines;
    }
}
