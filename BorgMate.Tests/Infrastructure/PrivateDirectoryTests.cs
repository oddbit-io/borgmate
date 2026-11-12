using BorgMate.Infrastructure.Platform;

namespace BorgMate.Tests.Infrastructure;

public class PrivateDirectoryTests
{
    [SkippableFact]
    public void Ensure_ExistingDirectoryReadableByOthers_BecomesOwnerOnly()
    {
        if (OperatingSystem.IsWindows())
            throw new SkipException("Unix permissions only.");
        const UnixFileMode ownerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
        using var parent = new TempDirectory();
        var path = Path.Combine(parent.Path, "BorgMate");
        Directory.CreateDirectory(path);
        File.SetUnixFileMode(path, ownerOnly | UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                                   UnixFileMode.OtherRead | UnixFileMode.OtherExecute);

        PrivateDirectory.Ensure(path);

        Assert.Equal(ownerOnly, File.GetUnixFileMode(path));
    }
}
