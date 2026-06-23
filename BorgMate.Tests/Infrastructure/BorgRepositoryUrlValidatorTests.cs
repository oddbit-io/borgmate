using BorgMate.Infrastructure.Borg;

namespace BorgMate.Tests.Infrastructure;

public class BorgRepositoryUrlValidatorTests
{
    [Theory]
    [InlineData("ssh://user@host.example.com//srv/borg")]
    [InlineData("sftp://user@host.example.com//srv/borg")]
    [InlineData("file:///srv/borg")]
    [InlineData("rest://repo.example.com/backups")]
    [InlineData("rclone:remote:path/to/repo")]
    [InlineData("s3:bucket/path/to/repo")]
    [InlineData("b2:bucket/path/to/repo")]
    public void IsSupportedBorg2Url_AcceptsSupportedSchemes(string value)
    {
        Assert.True(BorgRepositoryUrlValidator.IsSupportedBorg2Url(value));
    }

    [Theory]
    [InlineData("")]
    [InlineData("https://example.com/repo")]
    [InlineData("ssh://user:pass@host.example.com//srv/borg")]
    [InlineData("s3:access:secret@bucket/path")]
    [InlineData("rclone:user:token@remote:path")]
    public void IsSupportedBorg2Url_RejectsUnsupportedOrSecretUrls(string value)
    {
        Assert.False(BorgRepositoryUrlValidator.IsSupportedBorg2Url(value));
    }

    [Theory]
    [InlineData("ssh://user@host.example.com//srv/borg")]
    [InlineData("sftp://user@host.example.com//srv/borg")]
    public void UsesSshTransport_ReturnsTrueForSshLikeSchemes(string value)
    {
        Assert.True(BorgRepositoryUrlValidator.UsesSshTransport(value));
    }

    [Theory]
    [InlineData("file:///srv/borg")]
    [InlineData("rest://repo.example.com/backups")]
    [InlineData("s3:bucket/path")]
    [InlineData("rclone:remote:path")]
    public void UsesSshTransport_ReturnsFalseForNonSshSchemes(string value)
    {
        Assert.False(BorgRepositoryUrlValidator.UsesSshTransport(value));
    }

    [Fact]
    public void Redact_RemovesStandardUriUserInfo()
    {
        var redacted = BorgRepositoryUrlValidator.Redact("ssh://user:pass@host.example.com//srv/borg");

        Assert.Equal("ssh://***@host.example.com//srv/borg", redacted);
    }

    [Fact]
    public void Redact_RemovesCustomSchemeUserInfo()
    {
        var redacted = BorgRepositoryUrlValidator.Redact("s3:access:secret@bucket/path");

        Assert.Equal("s3:***@bucket/path", redacted);
    }
}
