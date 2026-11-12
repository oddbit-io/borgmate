using BorgMate.Infrastructure.Platform;

namespace BorgMate.Tests.Infrastructure;

public class ExecutableResolverTests
{
    [Fact]
    public void Resolve_FileOnlyInApplicationDirectory_IsNotPicked()
    {
        const string planted = "BorgMate.Tests.dll";
        Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, planted)));

        Assert.Throws<FileNotFoundException>(() => ExecutableResolver.Resolve(planted));
    }

    [Theory]
    [InlineData("/usr/local/bin/borg")]
    [InlineData("./borg")]
    public void Resolve_PathWithDirectory_IsKept(string fileName)
    {
        Assert.Equal(fileName, ExecutableResolver.Resolve(fileName));
    }

    [SkippableFact]
    public void Resolve_ProgramOnPath_ReturnsAbsolutePath()
    {
        BashHarness.RequireBash();

        var resolved = ExecutableResolver.Resolve("bash");

        Assert.True(Path.IsPathFullyQualified(resolved));
        Assert.True(File.Exists(resolved));
    }
}
