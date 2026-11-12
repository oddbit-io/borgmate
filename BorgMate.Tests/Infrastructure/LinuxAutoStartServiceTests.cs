using BorgMate.Infrastructure.Platform;

namespace BorgMate.Tests.Infrastructure;

public class LinuxAutoStartServiceTests
{
    [Theory]
    [InlineData("/home/me/Apps/BorgMate.AppImage", "\"/home/me/Apps/BorgMate.AppImage\"")]
    [InlineData("/home/me/My Apps/BorgMate.AppImage", "\"/home/me/My Apps/BorgMate.AppImage\"")]
    [InlineData("/opt/100%/BorgMate", "\"/opt/100%%/BorgMate\"")]
    [InlineData("/opt/$HOME/`id`/BorgMate", "\"/opt/\\\\$HOME/\\\\`id\\\\`/BorgMate\"")]
    [InlineData("/opt/a\"b/BorgMate", "\"/opt/a\\\\\"b/BorgMate\"")]
    public void QuoteExecArgument_KeepsPathOneLiteralArgument(string path, string expected)
    {
        Assert.Equal(expected, LinuxAutoStartService.QuoteExecArgument(path));
    }

    [Fact]
    public void QuoteExecArgument_LineBreak_IsRejected()
    {
        Assert.Throws<InvalidOperationException>(() =>
            LinuxAutoStartService.QuoteExecArgument("/opt/BorgMate\nHidden=true"));
    }
}
