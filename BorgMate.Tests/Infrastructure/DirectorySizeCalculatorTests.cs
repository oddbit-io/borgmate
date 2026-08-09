using BorgMate.Infrastructure.Platform;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace BorgMate.Tests.Infrastructure;

public class DirectorySizeCalculatorTests
{
    [Fact]
    public void Calculate_ExcludedFilesAndDirectories_AreNotCounted()
    {
        var root = Directory.CreateTempSubdirectory();
        try
        {
            File.WriteAllText(Path.Combine(root.FullName, "included.txt"), "1234");
            var excludedFile = Path.Combine(root.FullName, "excluded.txt");
            File.WriteAllText(excludedFile, "1234567");

            var excludedDirectory = root.CreateSubdirectory("cache");
            File.WriteAllText(Path.Combine(excludedDirectory.FullName, "cached.bin"), "123456");
            var similarlyNamedDirectory = root.CreateSubdirectory("cache-other");
            File.WriteAllText(Path.Combine(similarlyNamedDirectory.FullName, "kept.bin"), "12");

            var calculator = new DirectorySizeCalculator(
                Substitute.For<ILogger<DirectorySizeCalculator>>());

            var (totalSize, fileCount) = calculator.Calculate(
                [root.FullName],
                [excludedFile, excludedDirectory.FullName],
                CancellationToken.None);

            Assert.Equal(6, totalSize);
            Assert.Equal(2, fileCount);
        }
        finally
        {
            root.Delete(true);
        }
    }
}
