using BorgMate.Models;

namespace BorgMate.Tests;

public class ArchiveFileTreeBuilderTests
{
    private static string MakeJsonLine(string path, string type = "-", long size = 0) =>
        $$"""{"path":"{{path}}","type":"{{type}}","size":{{size}}}""";

    [Fact]
    public void Build_SingleFile()
    {
        var json = MakeJsonLine("readme.txt", size: 100);
        var roots = ArchiveFileTreeBuilder.Build(json);

        Assert.Single(roots);
        Assert.Equal("readme.txt", roots[0].Name);
        Assert.False(roots[0].IsDirectory);
        Assert.Equal(100, roots[0].Size);
        Assert.Equal(0, roots[0].Depth);
    }

    [Fact]
    public void Build_NestedFile_CreatesParentDirectories()
    {
        var json = MakeJsonLine("home/user/file.txt", size: 50);
        var roots = ArchiveFileTreeBuilder.Build(json);

        Assert.Single(roots);
        var home = roots[0];
        Assert.Equal("home", home.Name);
        Assert.True(home.IsDirectory);
        Assert.Equal(0, home.Depth);

        Assert.Single(home.Children);
        var user = home.Children[0];
        Assert.Equal("user", user.Name);
        Assert.True(user.IsDirectory);
        Assert.Equal(1, user.Depth);
        Assert.Equal(home, user.Parent);

        Assert.Single(user.Children);
        var file = user.Children[0];
        Assert.Equal("file.txt", file.Name);
        Assert.Equal(2, file.Depth);
        Assert.Equal(user, file.Parent);
    }

    [Fact]
    public void Build_DirectoriesAndFiles_SortedCorrectly()
    {
        var json = string.Join("\n",
            MakeJsonLine("b.txt", size: 10),
            MakeJsonLine("a_dir", "d"),
            MakeJsonLine("a.txt", size: 20),
            MakeJsonLine("a_dir/file.txt", size: 30));

        var roots = ArchiveFileTreeBuilder.Build(json);

        // Directories come first, then files, both alphabetical
        Assert.Equal(3, roots.Count);
        Assert.True(roots[0].IsDirectory);
        Assert.Equal("a_dir", roots[0].Name);
        Assert.Equal("a.txt", roots[1].Name);
        Assert.Equal("b.txt", roots[2].Name);
    }

    [Fact]
    public void Build_MultipleFilesInDirectory()
    {
        var json = string.Join("\n",
            MakeJsonLine("docs/b.txt", size: 10),
            MakeJsonLine("docs/a.txt", size: 20));

        var roots = ArchiveFileTreeBuilder.Build(json);
        Assert.Single(roots);
        var docs = roots[0];
        Assert.Equal(2, docs.Children.Count);
        Assert.Equal("a.txt", docs.Children[0].Name); // sorted
        Assert.Equal("b.txt", docs.Children[1].Name);
    }

    [Fact]
    public void Build_EmptyInput_ReturnsEmpty()
    {
        Assert.Empty(ArchiveFileTreeBuilder.Build(""));
    }

    [Fact]
    public void Build_MalformedJson_Skipped()
    {
        var json = "not json\n" + MakeJsonLine("file.txt", size: 1);
        var roots = ArchiveFileTreeBuilder.Build(json);

        Assert.Single(roots);
        Assert.Equal("file.txt", roots[0].Name);
    }

    [Fact]
    public void Build_Depth_SetCorrectly()
    {
        var json = MakeJsonLine("a/b/c/d.txt", size: 1);
        var roots = ArchiveFileTreeBuilder.Build(json);

        var a = roots[0]; // depth 0
        var b = a.Children[0]; // depth 1
        var c = b.Children[0]; // depth 2
        var d = c.Children[0]; // depth 3

        Assert.Equal(0, a.Depth);
        Assert.Equal(1, b.Depth);
        Assert.Equal(2, c.Depth);
        Assert.Equal(3, d.Depth);
    }

    [Fact]
    public void AutoExpand_NarrowPaths()
    {
        var json = string.Join("\n",
            MakeJsonLine("home", "d"),
            MakeJsonLine("home/user", "d"),
            MakeJsonLine("home/user/file.txt", size: 1));

        var roots = ArchiveFileTreeBuilder.Build(json);
        ArchiveFileNode.AutoExpand(roots);

        Assert.True(roots[0].IsExpanded); // home (1 child)
        Assert.True(roots[0].Children[0].IsExpanded); // user (1 child)
    }

    [Fact]
    public void AutoExpand_WidePaths_NotExpanded()
    {
        var json = string.Join("\n",
            MakeJsonLine("a", "d"),
            MakeJsonLine("b", "d"),
            MakeJsonLine("c", "d"),
            MakeJsonLine("d", "d"),
            MakeJsonLine("a/file.txt", size: 1));

        var roots = ArchiveFileTreeBuilder.Build(json);
        ArchiveFileNode.AutoExpand(roots);

        // 4 root nodes > 3, none expanded
        Assert.False(roots[0].IsExpanded);
    }
}
