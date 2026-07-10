using BorgMate.Domain.Repositories;

namespace BorgMate.Tests.Domain;

public class SmbLocationTests
{
    [Fact]
    public void TryParse_FullUrl_ParsesAllParts()
    {
        var location = SmbLocation.TryParse("smb://bob@nas.local/backups/laptop/repo");

        Assert.NotNull(location);
        Assert.Equal("nas.local", location!.Host);
        Assert.Equal("backups", location.Share);
        Assert.Equal("laptop/repo", location.PathInShare);
        Assert.Equal("bob", location.Username);
    }

    [Fact]
    public void TryParse_WithoutUserAndPath_LeavesThemEmpty()
    {
        var location = SmbLocation.TryParse("smb://nas/backups");

        Assert.NotNull(location);
        Assert.Equal("nas", location!.Host);
        Assert.Equal("backups", location.Share);
        Assert.Equal(string.Empty, location.PathInShare);
        Assert.Equal(string.Empty, location.Username);
    }

    [Fact]
    public void TryParse_TrailingSlash_Ignored()
    {
        var location = SmbLocation.TryParse("smb://nas/backups/");

        Assert.NotNull(location);
        Assert.Equal("backups", location!.Share);
        Assert.Equal(string.Empty, location.PathInShare);
    }

    [Fact]
    public void TryParse_UnescapesShareAndPath()
    {
        var location = SmbLocation.TryParse("smb://nas/My%20Share/sub%20dir");

        Assert.NotNull(location);
        Assert.Equal("My Share", location!.Share);
        Assert.Equal("sub dir", location.PathInShare);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("/local/path")]
    [InlineData("ssh://host/repo")]
    [InlineData("smb://")]
    [InlineData("smb://host")]
    [InlineData("smb://host/")]
    [InlineData("smb://user:secret@host/share")]
    public void TryParse_RejectsInvalidValues(string value)
    {
        Assert.Null(SmbLocation.TryParse(value));
    }

    [Fact]
    public void ToUrl_RoundTripsThroughTryParse()
    {
        var original = new SmbLocation
        {
            Host = "nas.local",
            Share = "My Share",
            PathInShare = "laptop/repo",
            Username = "bob"
        };

        var restored = SmbLocation.TryParse(original.ToUrl());

        Assert.Equal(original, restored);
    }

    [Fact]
    public void ToUrl_OmitsEmptyUserAndPath()
    {
        var location = new SmbLocation { Host = "nas", Share = "backups" };

        Assert.Equal("smb://nas/backups", location.ToUrl());
    }

    [Fact]
    public void ToUrl_EscapesSpaces()
    {
        var location = new SmbLocation { Host = "nas", Share = "My Share" };

        Assert.Equal("smb://nas/My%20Share", location.ToUrl());
    }

    [Fact]
    public void KeychainKey_LowercasesHostAndShare()
    {
        var location = new SmbLocation { Host = "NAS.Local", Share = "Backups" };

        Assert.Equal("smb:nas.local/backups", location.KeychainKey);
    }

    [Fact]
    public void DisplayName_FormatsHostAndShare()
    {
        var location = new SmbLocation { Host = "nas", Share = "backups" };

        Assert.Equal("//nas/backups", location.DisplayName);
    }
}
