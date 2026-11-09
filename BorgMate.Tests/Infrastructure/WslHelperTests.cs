using Microsoft.Extensions.Logging;
using NSubstitute;
using WslHelper = BorgMate.Infrastructure.Platform.WslHelper;

namespace BorgMate.Tests.Infrastructure;

public class WslHelperTests
{
    [Fact]
    public async Task IsAvailableAsync_AwaitsProbeWithoutBlockingAndCachesResult()
    {
        var probe = new ControllableWslAvailabilityProbe();
        var helper = new WslHelper(Substitute.For<ILogger<WslHelper>>(), probe, true);

        var availabilityTask = helper.IsAvailableAsync();

        Assert.False(availabilityTask.IsCompleted);
        Assert.False(helper.IsAvailable);

        probe.Complete(true);

        Assert.True(await availabilityTask);
        Assert.True(helper.IsAvailable);
        Assert.True(await helper.IsAvailableAsync());
        Assert.Equal(1, probe.Calls);
    }

    [Fact]
    public async Task IsAvailableAsync_NotRequired_DoesNotCallProbe()
    {
        var probe = new ControllableWslAvailabilityProbe();
        var helper = new WslHelper(Substitute.For<ILogger<WslHelper>>(), probe, false);

        Assert.False(await helper.IsAvailableAsync());
        Assert.Equal(0, probe.Calls);
    }

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
        var (fileName, arguments) = WslHelper.WrapCommand("borg", "create /repo::archive /data");

        Assert.Equal("wsl", fileName);
        Assert.Contains("bash", arguments);
        Assert.Contains("create /repo::archive /data", arguments);
    }

    [SkippableFact]
    public async Task BuildCopySshKeyArguments_ShellMetacharactersInPathsAreCopiedLiterally()
    {
        BashHarness.RequireBash();

        using var temp = new TempDirectory();
        var directory = temp.Path;
        var source = directory + "/key$(touch $MARKER1)`touch $MARKER2`";
        var destination = directory + "/home/.ssh/copy$(touch $MARKER3)";
        await File.WriteAllTextAsync(source, "key-data");

        var result = await BashHarness.RunWslArgumentsAsync(
            WslHelper.BuildCopySshKeyArguments(source, destination),
            new Dictionary<string, string>
            {
                ["HOME"] = directory + "/home",
                ["MARKER1"] = directory + "/marker1",
                ["MARKER2"] = directory + "/marker2",
                ["MARKER3"] = directory + "/marker3"
            });

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("key-data", await File.ReadAllTextAsync(destination));
        Assert.False(File.Exists(directory + "/marker1"));
        Assert.False(File.Exists(directory + "/marker2"));
        Assert.False(File.Exists(directory + "/marker3"));
    }

    [Fact]
    public void ForwardEnvironment_ListsVariablesInWslEnv()
    {
        var environment = WslHelper.ForwardEnvironment(new Dictionary<string, string>
        {
            ["BORG_PASSPHRASE"] = "secret",
            ["BORG_EXIT_CODES"] = "modern"
        });

        Assert.Equal("secret", environment["BORG_PASSPHRASE"]);
        Assert.Equal("modern", environment["BORG_EXIT_CODES"]);
        Assert.EndsWith("BORG_PASSPHRASE:BORG_EXIT_CODES", environment["WSLENV"]);
    }
}
