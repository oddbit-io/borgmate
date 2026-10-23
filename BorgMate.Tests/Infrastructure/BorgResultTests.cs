using BorgMate.Infrastructure.Borg;

namespace BorgMate.Tests.Infrastructure;

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
    public void ErrorCode_Null_WhenSuccess()
    {
        var result = new BorgResult(0, "ok", "some stderr");
        Assert.Null(result.ErrorCode);
    }

    [Fact]
    public void ErrorCode_MappedFromExitCode_WhenFailed()
    {
        var result = new BorgResult(80, "", "Connection closed by remote host");
        Assert.Equal(BorgModernExitCode.ConnectionClosed, result.ErrorCode);
    }

    [Fact]
    public void ErrorMessage_UsesUnknownMessageAndStderr_WhenExitCodeIsUnknown()
    {
        var result = new BorgResult(150, "", "something unexpected");
        Assert.Equal(
            $"Borg failed with unknown exit code 150.{Environment.NewLine}{Environment.NewLine}something unexpected",
            result.ErrorMessage);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    [InlineData(105)]
    [InlineData(127)]
    public void Completed_WithWarnings_WhenExitCodeIsBorgWarning(int exitCode)
    {
        var result = new BorgResult(exitCode, "", "warning");
        Assert.False(result.Success);
        Assert.True(result.HasWarnings);
        Assert.True(result.Completed);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(99)]
    [InlineData(128)]
    [InlineData(-1)]
    public void Completed_False_WhenExitCodeIsBorgError(int exitCode)
    {
        var result = new BorgResult(exitCode, "", "error");
        Assert.False(result.HasWarnings);
        Assert.False(result.Completed);
    }

    [Fact]
    public void ErrorMessage_UsesGenericWarning_WhenWarningCodeIsUnknown()
    {
        var result = new BorgResult(119, "", "something skipped");
        Assert.Equal(BorgModernExitCode.Warning, result.ErrorCode);
        Assert.Equal(
            $"Borg completed with warnings.{Environment.NewLine}{Environment.NewLine}something skipped",
            result.ErrorMessage);
    }

    [Fact]
    public void ErrorMessage_UsesExactCodeMessageAndStderr_WhenExitCodeIsKnown()
    {
        var result = new BorgResult(52, "", "Passphrase supplied in BORG_PASSPHRASE is incorrect.");
        Assert.Equal(
            $"Wrong Borg repository passphrase.{Environment.NewLine}{Environment.NewLine}Passphrase supplied in BORG_PASSPHRASE is incorrect.",
            result.ErrorMessage);
    }

    [Theory]
    [InlineData("sh: borg: command not found")]
    [InlineData("sh: borg: not found")]
    public void ErrorMessage_ReportsMissingRemoteBorg_WhenConnectionClosedWithNotFound(string shellError)
    {
        var stderr = $"{shellError}\nConnection closed by remote host. Is borg working on the server?";
        var result = new BorgResult(81, "", stderr);

        Assert.StartsWith("Borg was not found on the remote server.", result.ErrorMessage);
        Assert.Contains(shellError, result.ErrorMessage);
    }

    [Fact]
    public void ErrorMessage_KeepsConnectionClosedHeadline_WhenStderrLacksNotFound()
    {
        var result = new BorgResult(80, "", "Connection reset by peer");
        Assert.StartsWith("Borg remote connection was closed.", result.ErrorMessage);
    }

    [Fact]
    public void ErrorMessage_RemovesProgressNoise_WhenUnknownType()
    {
        var stderr = string.Join(
            "\n",
            "0 B O 0 B C 0 B D 0 N Users/user/Documents",
            "Initializing cache transaction: Reading config",
            "7.77 GB O 7.57 GB C 0 B D 1190 N Users/user/Documents/photo.jpg",
            "borg: /Users/user/Documents/photo.jpg: file changed while reading");
        var result = new BorgResult(150, "", stderr);

        Assert.Equal(
            $"Borg failed with unknown exit code 150.{Environment.NewLine}{Environment.NewLine}borg: /Users/user/Documents/photo.jpg: file changed while reading",
            result.ErrorMessage);
    }

    [Fact]
    public void ErrorMessage_UsesLastDiagnosticLines_WhenUnknownType()
    {
        var stderr = string.Join(
            "\n",
            "first detail",
            "second detail",
            "third detail",
            "fourth detail",
            "fifth detail",
            "sixth detail",
            "seventh detail",
            "eighth detail",
            "ninth detail");
        var result = new BorgResult(1, "", stderr);

        var expected = string.Join(
            Environment.NewLine,
            "Borg completed with warnings.",
            "",
            "second detail",
            "third detail",
            "fourth detail",
            "fifth detail",
            "sixth detail",
            "seventh detail",
            "eighth detail",
            "ninth detail");
        Assert.Equal(expected, result.ErrorMessage);
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
