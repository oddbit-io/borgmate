using BorgMate.Models;
using BorgMate.Services.Borg;

namespace BorgMate.Tests;

public class BorgDiffParserTests
{
    [Fact]
    public void ParseChangedPaths_AddedFile()
    {
        var output = "added       2.5 kB home/user/newfile.txt\n";
        var result = BorgDiffParser.ParseChangedPaths(output);

        Assert.Single(result);
        Assert.Equal(FileChangeKind.Added, result["home/user/newfile.txt"]);
    }

    [Fact]
    public void ParseChangedPaths_ModifiedFile()
    {
        var output = "    +77 B    -66 B home/user/changed.txt\n";
        var result = BorgDiffParser.ParseChangedPaths(output);

        Assert.Single(result);
        Assert.Equal(FileChangeKind.Modified, result["home/user/changed.txt"]);
    }

    [Fact]
    public void ParseChangedPaths_MultipleFiles()
    {
        var output = """
            added       1.0 kB home/user/new.txt
                +100 B    -50 B home/user/modified.txt
            added     512.0 kB home/user/another.bin
            """;
        var result = BorgDiffParser.ParseChangedPaths(output);

        Assert.Equal(3, result.Count);
        Assert.Equal(FileChangeKind.Added, result["home/user/new.txt"]);
        Assert.Equal(FileChangeKind.Modified, result["home/user/modified.txt"]);
        Assert.Equal(FileChangeKind.Added, result["home/user/another.bin"]);
    }

    [Fact]
    public void ParseChangedPaths_EmptyOutput()
    {
        var result = BorgDiffParser.ParseChangedPaths("");
        Assert.Empty(result);
    }

    [Fact]
    public void ParseChangedPaths_UnrecognizedLines_Ignored()
    {
        var output = """
            some random header
            ---
            added       1.0 kB home/user/file.txt
            totally unrelated line
            """;
        var result = BorgDiffParser.ParseChangedPaths(output);

        Assert.Single(result);
        Assert.Equal(FileChangeKind.Added, result["home/user/file.txt"]);
    }

    [Fact]
    public void ParseChangedPaths_PathWithSpaces()
    {
        var output = "added       2.0 MB home/user/my documents/report final.pdf\n";
        var result = BorgDiffParser.ParseChangedPaths(output);

        Assert.Single(result);
        Assert.Equal(FileChangeKind.Added, result["home/user/my documents/report final.pdf"]);
    }
}
