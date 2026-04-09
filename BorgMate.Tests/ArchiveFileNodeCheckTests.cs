using BorgMate.Models;

namespace BorgMate.Tests;

public class ArchiveFileNodeCheckTests
{
    private static ArchiveFileNode MakeDir(string name, int depth = 0) =>
        new() { Name = name, FullPath = name, IsDirectory = true, Depth = depth };

    private static ArchiveFileNode MakeFile(string name, int depth = 0) =>
        new() { Name = name, FullPath = name, Depth = depth };

    private static void AddChild(ArchiveFileNode parent, ArchiveFileNode child)
    {
        child.Parent = parent;
        parent.Children.Add(child);
    }

    [Fact]
    public void CheckParent_ChecksAllChildren()
    {
        var parent = MakeDir("dir");
        var child1 = MakeFile("a.txt", 1);
        var child2 = MakeFile("b.txt", 1);
        AddChild(parent, child1);
        AddChild(parent, child2);

        parent.IsChecked = true;

        Assert.True(child1.IsChecked);
        Assert.True(child2.IsChecked);
    }

    [Fact]
    public void UncheckParent_UnchecksAllChildren()
    {
        var parent = MakeDir("dir");
        var child1 = MakeFile("a.txt", 1);
        var child2 = MakeFile("b.txt", 1);
        AddChild(parent, child1);
        AddChild(parent, child2);

        parent.IsChecked = true;
        parent.IsChecked = false;

        Assert.False(child1.IsChecked);
        Assert.False(child2.IsChecked);
    }

    [Fact]
    public void CheckChild_ParentBecomesIndeterminate()
    {
        var parent = MakeDir("dir");
        var child1 = MakeFile("a.txt", 1);
        var child2 = MakeFile("b.txt", 1);
        AddChild(parent, child1);
        AddChild(parent, child2);

        child1.IsChecked = true;

        Assert.Null(parent.IsChecked); // indeterminate
    }

    [Fact]
    public void CheckAllChildren_ParentBecomesChecked()
    {
        var parent = MakeDir("dir");
        var child1 = MakeFile("a.txt", 1);
        var child2 = MakeFile("b.txt", 1);
        AddChild(parent, child1);
        AddChild(parent, child2);

        child1.IsChecked = true;
        child2.IsChecked = true;

        Assert.True(parent.IsChecked);
    }

    [Fact]
    public void UncheckAllChildren_ParentBecomesUnchecked()
    {
        var parent = MakeDir("dir");
        var child1 = MakeFile("a.txt", 1);
        var child2 = MakeFile("b.txt", 1);
        AddChild(parent, child1);
        AddChild(parent, child2);

        parent.IsChecked = true;
        child1.IsChecked = false;
        child2.IsChecked = false;

        Assert.False(parent.IsChecked);
    }

    [Fact]
    public void DeepNesting_PropagatesUp()
    {
        var root = MakeDir("root");
        var mid = MakeDir("mid", 1);
        var leaf = MakeFile("file.txt", 2);
        AddChild(root, mid);
        AddChild(mid, leaf);

        leaf.IsChecked = true;

        // mid has one child (leaf) which is checked → mid becomes checked
        // root has one child (mid) which is checked → root becomes checked
        Assert.True(mid.IsChecked);
        Assert.True(root.IsChecked);
    }

    [Fact]
    public void CheckParent_PropagatesDeep()
    {
        var root = MakeDir("root");
        var mid = MakeDir("mid", 1);
        var leaf = MakeFile("file.txt", 2);
        AddChild(root, mid);
        AddChild(mid, leaf);

        root.IsChecked = true;

        Assert.True(mid.IsChecked);
        Assert.True(leaf.IsChecked);
    }

    [Fact]
    public void CheckTreeChanged_Fires()
    {
        var fired = false;
        ArchiveFileNode.CheckTreeChanged += () => fired = true;
        try
        {
            var node = MakeFile("test.txt");
            node.IsChecked = true;
            Assert.True(fired);
        }
        finally
        {
            // Clean up static event to not affect other tests
            ArchiveFileNode.CheckTreeChanged -= () => fired = true;
        }
    }

    [Fact]
    public void CanExpand_DirectoryWithChildren_True()
    {
        var dir = MakeDir("dir");
        AddChild(dir, MakeFile("file.txt", 1));
        Assert.True(dir.CanExpand);
    }

    [Fact]
    public void CanExpand_EmptyDirectory_False()
    {
        Assert.False(MakeDir("empty").CanExpand);
    }

    [Fact]
    public void CanExpand_File_False()
    {
        Assert.False(MakeFile("file.txt").CanExpand);
    }
}
