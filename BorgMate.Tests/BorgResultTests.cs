using BorgMate.Services.Borg;

namespace BorgMate.Tests;

public class BorgResultTests
{
    [Fact]
    public void Success_True_WhenExitCodeZero()
    {
        var result = new BorgResult(0, "output", "");
        Assert.True(result.Success);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(-1)]
    [InlineData(128)]
    public void Success_False_WhenExitCodeNonZero(int exitCode)
    {
        var result = new BorgResult(exitCode, "", "error");
        Assert.False(result.Success);
    }

    [Fact]
    public void ErrorType_Unknown_WhenSuccess()
    {
        var result = new BorgResult(0, "ok", "some stderr");
        Assert.Equal(BorgErrorType.Unknown, result.ErrorType);
    }

    [Fact]
    public void ErrorType_Classified_WhenFailed()
    {
        var result = new BorgResult(2, "", "Connection refused");
        Assert.Equal(BorgErrorType.SshConnectionRefused, result.ErrorType);
    }

    [Fact]
    public void ErrorMessage_FallsBackToStderr_WhenUnknownType()
    {
        var result = new BorgResult(1, "", "something unexpected");
        Assert.Equal("something unexpected", result.ErrorMessage);
    }

    [Fact]
    public void WasCancelled_DefaultsFalse()
    {
        var result = new BorgResult(0, "", "");
        Assert.False(result.WasCancelled);
    }

    [Fact]
    public void WasCancelled_CanBeSetTrue()
    {
        var result = new BorgResult(-1, "", "", WasCancelled: true);
        Assert.True(result.WasCancelled);
    }

    [Fact]
    public void Record_Equality_SameValues()
    {
        var a = new BorgResult(0, "out", "err");
        var b = new BorgResult(0, "out", "err");
        Assert.Equal(a, b);
    }

    [Fact]
    public void Record_Inequality_DifferentExitCode()
    {
        var a = new BorgResult(0, "out", "err");
        var b = new BorgResult(1, "out", "err");
        Assert.NotEqual(a, b);
    }
}
