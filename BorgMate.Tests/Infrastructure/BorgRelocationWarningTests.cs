using BorgMate.Domain.Repositories;
using BorgMate.Infrastructure.Borg;

namespace BorgMate.Tests.Infrastructure;

public class BorgRelocationWarningTests
{
    [Fact]
    public void TryParse_Borg14RelocationAbort_ReturnsReportedLocations()
    {
        const string stderr =
            "Warning: The repository at location ssh://backup@nas:2222/./repos/docs was previously located at /mnt/e/repos/docs\n" +
            "Do you want to continue? [yN] no (from BORG_RELOCATED_REPO_ACCESS_IS_OK)\n" +
            "Aborting.\n" +
            "Repository access aborted\n";

        var relocation = BorgRelocationWarning.TryParse(stderr);

        Assert.Equal(
            new RepositoryRelocation
            {
                Location = "ssh://backup@nas:2222/./repos/docs",
                PreviousLocation = "/mnt/e/repos/docs"
            },
            relocation);
    }

    [Fact]
    public void TryParse_CrLfLineEndings_ExcludesCarriageReturn()
    {
        var relocation = BorgRelocationWarning.TryParse(
            "Warning: The repository at location /repo-new was previously located at /repo-old\r\nAborting.\r\n");

        Assert.Equal("/repo-old", relocation?.PreviousLocation);
    }

    [Fact]
    public void TryParse_OtherAbort_ReturnsNull()
    {
        Assert.Null(BorgRelocationWarning.TryParse("Repository access aborted\n"));
    }
}
