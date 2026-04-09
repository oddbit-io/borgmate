using BorgMate.Services;

namespace BorgMate.Tests;

public class SshAgentHelperTests
{
    [Fact]
    public void EscapeForExpect_PlainString_Unchanged()
    {
        Assert.Equal("simplepassword", SshAgentHelper.EscapeForExpect("simplepassword"));
    }

    [Fact]
    public void EscapeForExpect_Backslashes_Escaped()
    {
        Assert.Equal("pass\\\\word", SshAgentHelper.EscapeForExpect("pass\\word"));
    }

    [Fact]
    public void EscapeForExpect_DoubleQuotes_Escaped()
    {
        Assert.Equal("pass\\\"word", SshAgentHelper.EscapeForExpect("pass\"word"));
    }

    [Fact]
    public void EscapeForExpect_Brackets_Escaped()
    {
        Assert.Equal("pass\\[word\\]", SshAgentHelper.EscapeForExpect("pass[word]"));
    }

    [Fact]
    public void EscapeForExpect_DollarSign_Escaped()
    {
        Assert.Equal("pass\\$word", SshAgentHelper.EscapeForExpect("pass$word"));
    }

    [Fact]
    public void EscapeForExpect_AllSpecialChars_Escaped()
    {
        var input = "p\\a\"s[s$w]ord";
        var expected = "p\\\\a\\\"s\\[s\\$w\\]ord";
        Assert.Equal(expected, SshAgentHelper.EscapeForExpect(input));
    }

    [Fact]
    public void BuildExpectScript_ContainsSpawn()
    {
        var script = SshAgentHelper.BuildExpectScript("/home/user/.ssh/id_ed25519", "mypass");
        Assert.Contains("spawn ssh-add", script);
        Assert.Contains("/home/user/.ssh/id_ed25519", script);
    }

    [Fact]
    public void BuildExpectScript_ContainsPassphrase()
    {
        var script = SshAgentHelper.BuildExpectScript("/key", "secret123");
        Assert.Contains("secret123", script);
    }

    [Fact]
    public void BuildExpectScript_EscapesSpecialCharsInPassphrase()
    {
        var script = SshAgentHelper.BuildExpectScript("/key", "p$a[ss]");
        Assert.Contains("p\\$a\\[ss\\]", script);
        Assert.DoesNotContain("p$a[ss]", script);
    }

    [Fact]
    public void BuildExpectScript_EscapesKeyPathQuotes()
    {
        var script = SshAgentHelper.BuildExpectScript("/path/with \"quotes\"", "pass");
        Assert.Contains("/path/with \\\"quotes\\\"", script);
    }
}
