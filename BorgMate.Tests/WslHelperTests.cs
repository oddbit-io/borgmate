using BorgMate.Services;

namespace BorgMate.Tests;

public class WslHelperTests
{
    [Theory]
    [InlineData(@"C:\Users\foo\Documents", "/mnt/c/Users/foo/Documents")]
    [InlineData(@"D:\backup\repo", "/mnt/d/backup/repo")]
    [InlineData(@"C:/Users/foo", "/mnt/c/Users/foo")]
    [InlineData(@"c:\data", "/mnt/c/data")]
    public void ToWslPath_ConvertsDriveLetter(string input, string expected)
    {
        Assert.Equal(expected, WslHelper.ToWslPath(input));
    }

    [Theory]
    [InlineData("/home/user/backup")]
    [InlineData("/mnt/c/Users")]
    public void ToWslPath_UnixPath_Unchanged(string input)
    {
        Assert.Equal(input, WslHelper.ToWslPath(input));
    }

    [Theory]
    [InlineData("user@host:/data/borg")]
    [InlineData("root@10.0.0.1:/backup")]
    public void ToWslPath_SshPath_Unchanged(string input)
    {
        Assert.Equal(input, WslHelper.ToWslPath(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(null)]
    public void ToWslPath_EmptyOrNull_ReturnsAsIs(string? input)
    {
        Assert.Equal(input, WslHelper.ToWslPath(input!));
    }

    [Theory]
    [InlineData("relative/path", "relative/path")]
    [InlineData(@"relative\path", "relative/path")]
    public void ToWslPath_RelativePath_ConvertsBackslashes(string input, string expected)
    {
        Assert.Equal(expected, WslHelper.ToWslPath(input));
    }

    [Fact]
    public void ShellEscape_BasicString()
    {
        Assert.Equal("$'hello'", WslHelper.ShellEscapePublic("hello"));
    }

    [Fact]
    public void ShellEscape_SingleQuotes()
    {
        Assert.Equal("$'it\\'s'", WslHelper.ShellEscapePublic("it's"));
    }

    [Fact]
    public void ShellEscape_Backslashes()
    {
        Assert.Equal("$'C:\\\\Users'", WslHelper.ShellEscapePublic("C:\\Users"));
    }

    [Fact]
    public void ShellEscape_Newlines()
    {
        Assert.Equal("$'line1\\nline2'", WslHelper.ShellEscapePublic("line1\nline2"));
    }

    [Fact]
    public void WrapCommand_ProducesCorrectStructure()
    {
        // WrapCommand is only meaningful on Windows/WSL, but we can test the output format
        // by calling it — on non-Windows it runs borg directly
        if (!OperatingSystem.IsWindows())
        {
            // On non-Windows, WrapCommand wraps with wsl -- bash -c
            // We can't test this without Windows, so just verify ToWslPath works
            return;
        }

        var (fileName, arguments) = WslHelper.WrapCommand(
            "borg", "create /repo::archive /data",
            new() { ["BORG_PASSPHRASE"] = "secret" });

        Assert.Equal("wsl", fileName);
        Assert.Contains("bash", arguments);
        Assert.Contains("BORG_PASSPHRASE", arguments);
    }
}
