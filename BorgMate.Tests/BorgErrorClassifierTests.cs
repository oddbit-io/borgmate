using BorgMate.Services.Borg;

namespace BorgMate.Tests;

public class BorgErrorClassifierTests
{
    [Theory]
    [InlineData("Host key verification failed", BorgErrorType.SshHostKeyVerificationFailed)]
    [InlineData("Permission denied (publickey)", BorgErrorType.SshPermissionDenied)]
    [InlineData("ssh: connect to host example.com: Connection refused", BorgErrorType.SshConnectionRefused)]
    [InlineData("ssh: connect to host example.com: Connection timed out", BorgErrorType.SshConnectionTimedOut)]
    [InlineData("ssh: Operation timed out", BorgErrorType.SshConnectionTimedOut)]
    [InlineData("ssh: connect to host example.com: No route to host", BorgErrorType.SshNoRouteToHost)]
    [InlineData("ssh: Could not resolve hostname example.com", BorgErrorType.SshHostnameNotFound)]
    [InlineData("ssh: Name or service not known", BorgErrorType.SshHostnameNotFound)]
    [InlineData("Connection closed by remote host", BorgErrorType.SshConnectionReset)]
    [InlineData("Connection reset by peer", BorgErrorType.SshConnectionReset)]
    [InlineData("Write failed: Broken pipe", BorgErrorType.SshConnectionReset)]
    [InlineData("ssh: /path/to/key: No such file or directory", BorgErrorType.SshKeyFileNotFound)]
    public void Classify_SshErrors(string stderr, BorgErrorType expected)
    {
        Assert.Equal(expected, BorgErrorClassifier.Classify(stderr));
    }

    [Theory]
    [InlineData("Repository /data/repo does not exist", BorgErrorType.RepositoryNotFound)]
    [InlineData("A repository already exists at /data/repo", BorgErrorType.RepositoryAlreadyExists)]
    [InlineData("Wrong passphrase for key", BorgErrorType.WrongPassphrase)]
    [InlineData("Incorrect passphrase", BorgErrorType.WrongPassphrase)]
    [InlineData("passphrase supplied for key is incorrect", BorgErrorType.WrongPassphrase)]
    [InlineData("decryption failed", BorgErrorType.WrongPassphrase)]
    [InlineData("Failed to create/acquire the lock (exclusive)", BorgErrorType.RepositoryLocked)]
    [InlineData("This is not a valid repository", BorgErrorType.InvalidRepository)]
    [InlineData("Incompatible repository", BorgErrorType.IncompatibleRepository)]
    [InlineData("No space left on device", BorgErrorType.NoSpaceLeftOnDevice)]
    [InlineData("borg: command not found", BorgErrorType.BorgBinaryNotFound)]
    public void Classify_BorgErrors(string stderr, BorgErrorType expected)
    {
        Assert.Equal(expected, BorgErrorClassifier.Classify(stderr));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("some random output")]
    public void Classify_UnknownErrors(string stderr)
    {
        Assert.Equal(BorgErrorType.Unknown, BorgErrorClassifier.Classify(stderr));
    }

    [Fact]
    public void Classify_Null_ReturnsUnknown()
    {
        Assert.Equal(BorgErrorType.Unknown, BorgErrorClassifier.Classify(null!));
    }

    [Fact]
    public void IsWrongPassphrase_True()
    {
        Assert.True(BorgErrorClassifier.IsWrongPassphrase(BorgErrorType.WrongPassphrase));
    }

    [Fact]
    public void IsWrongPassphrase_False()
    {
        Assert.False(BorgErrorClassifier.IsWrongPassphrase(BorgErrorType.SshConnectionRefused));
    }

    [Theory]
    [InlineData(BorgErrorType.SshConnectionReset)]
    [InlineData(BorgErrorType.SshConnectionTimedOut)]
    [InlineData(BorgErrorType.SshConnectionRefused)]
    [InlineData(BorgErrorType.SshNoRouteToHost)]
    public void IsTransientSshError_True(BorgErrorType errorType)
    {
        Assert.True(BorgErrorClassifier.IsTransientSshError(errorType));
    }

    [Theory]
    [InlineData(BorgErrorType.SshPermissionDenied)]
    [InlineData(BorgErrorType.WrongPassphrase)]
    [InlineData(BorgErrorType.Unknown)]
    [InlineData(BorgErrorType.RepositoryNotFound)]
    public void IsTransientSshError_False(BorgErrorType errorType)
    {
        Assert.False(BorgErrorClassifier.IsTransientSshError(errorType));
    }

    [Theory]
    [InlineData(BorgErrorType.SshConnectionReset)]
    [InlineData(BorgErrorType.SshConnectionTimedOut)]
    [InlineData(BorgErrorType.SshConnectionRefused)]
    [InlineData(BorgErrorType.SshNoRouteToHost)]
    [InlineData(BorgErrorType.RepositoryLocked)]
    public void IsRetryable_True(BorgErrorType errorType)
    {
        Assert.True(BorgErrorClassifier.IsRetryable(errorType));
    }

    [Theory]
    [InlineData(BorgErrorType.SshPermissionDenied)]
    [InlineData(BorgErrorType.WrongPassphrase)]
    [InlineData(BorgErrorType.Unknown)]
    [InlineData(BorgErrorType.RepositoryNotFound)]
    public void IsRetryable_False(BorgErrorType errorType)
    {
        Assert.False(BorgErrorClassifier.IsRetryable(errorType));
    }
}
