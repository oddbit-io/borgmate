using System.Diagnostics;
using System.Text;
using System.Xml.Linq;
using BorgMate.Infrastructure.Notifications;

namespace BorgMate.Tests.Infrastructure;

public class WindowsNotificationServiceTests
{
    private const string UnusedAppUserModelId = "BorgMate.Tests.NoSuchApp";
    private const string MarkerVariable = "BORGMATE_TEST_MARKER";
    private const int ProcessTimeoutMs = 60_000;
    private const string NotWindowsReason = "Toast notifications run only on Windows.";

    public static TheoryData<string> SingleQuoteCharacters => new()
    {
        "'",
        "\u2018",
        "\u2019",
        "\u201A",
        "\u201B"
    };

    [SkippableTheory]
    [MemberData(nameof(SingleQuoteCharacters))]
    public void BuildToastStartInfo_QuoteInTitle_DoesNotExecuteInjectedCode(string quote)
    {
        if (!OperatingSystem.IsWindows())
            throw new SkipException(NotWindowsReason);

        var dir = Directory.CreateTempSubdirectory("bm-toast-");
        try
        {
            var marker = Path.Combine(dir.FullName, "marker");
            var title = $"x{quote}); New-Item -ItemType File -Path $env:{MarkerVariable} | Out-Null #";
            var startInfo = WindowsNotificationService.BuildToastStartInfo("ignored", null, title, title);
            startInfo.Environment[WindowsNotificationService.ToastAppIdVariable] = UnusedAppUserModelId;
            startInfo.Environment[MarkerVariable] = marker;

            using var process = Process.Start(startInfo);
            Assert.NotNull(process);
            Assert.True(process.WaitForExit(ProcessTimeoutMs));

            Assert.False(File.Exists(marker));
        }
        finally
        {
            dir.Delete(true);
        }
    }

    [SkippableFact]
    public void BuildToastStartInfo_DifferentTexts_ScriptIsIdenticalAndHasNoNotificationData()
    {
        if (!OperatingSystem.IsWindows())
            throw new SkipException(NotWindowsReason);

        const string title1 = "Andrei\u2019s laptop";
        const string body1 = "first '\" body";
        var first = WindowsNotificationService.BuildToastStartInfo("id", null, title1, body1);
        var second = WindowsNotificationService.BuildToastStartInfo("id", null, "other", "text");

        Assert.Equal(first.Arguments, second.Arguments);
        var script = DecodeScript(first.Arguments);
        Assert.Equal(WindowsNotificationService.ToastScript, script);
        Assert.DoesNotContain("Andrei", script);
        Assert.DoesNotContain("first", script);
    }

    [SkippableFact]
    public void BuildToastStartInfo_Texts_AreCarriedAsEscapedXmlInEnvironment()
    {
        if (!OperatingSystem.IsWindows())
            throw new SkipException(NotWindowsReason);

        var startInfo = WindowsNotificationService.BuildToastStartInfo("my.app", null, "a<b&c", "d>e");

        var xml = startInfo.Environment[WindowsNotificationService.ToastXmlVariable];
        Assert.NotNull(xml);
        var texts = XDocument.Parse(xml).Descendants("text").Select(t => t.Value).ToList();
        Assert.Contains(texts, t => t.EndsWith("a<b&c"));
        Assert.Contains("d>e", texts);
        Assert.Equal("my.app", startInfo.Environment[WindowsNotificationService.ToastAppIdVariable]);
    }

    [SkippableFact]
    public void BuildToastStartInfo_IconPathWithAmpersand_YieldsWellFormedXml()
    {
        if (!OperatingSystem.IsWindows())
            throw new SkipException(NotWindowsReason);

        var startInfo = WindowsNotificationService.BuildToastStartInfo("id", @"C:\Users\A&B\icon.png", "t", "b");

        var xml = startInfo.Environment[WindowsNotificationService.ToastXmlVariable];
        Assert.NotNull(xml);
        var image = XDocument.Parse(xml).Descendants("image").Single();
        Assert.Equal("file:///C:/Users/A&B/icon.png", image.Attribute("src")?.Value);
    }

    private static string DecodeScript(string arguments)
    {
        var encoded = arguments[(arguments.LastIndexOf(' ') + 1)..];
        return Encoding.Unicode.GetString(Convert.FromBase64String(encoded));
    }
}
