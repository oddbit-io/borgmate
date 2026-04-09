using System.IO;
using System.Reflection;

namespace BorgMate.Views;

public partial class LicenseWindow : ModalWindow
{
    public LicenseWindow()
    {
        InitializeComponent();

        var dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        var path = Path.Combine(dir, "COPYING");
        var lines = File.Exists(path) ? File.ReadAllLines(path) : ["File not found."];
        LinesList.ItemsSource = lines;
    }
}
