using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BorgMate.Models;

/// <summary>Change status from borg diff comparison against the previous archive.</summary>
public enum FileChangeKind { None, Added, Modified }

public partial class ArchiveFileNode : ObservableObject
{
    public required string Name { get; init; }
    public required string FullPath { get; init; }
    public bool IsDirectory { get; init; }
    public long Size { get; init; }
    public DateTime? ModifiedAt { get; init; }
    public int Depth { get; init; }
    public ArchiveFileNode? Parent { get; set; }

    [ObservableProperty]
    private FileChangeKind _changeKind;

    [ObservableProperty]
    private bool? _isChecked = false;

    [ObservableProperty]
    private bool _isExpanded;

    public bool CanExpand => IsDirectory && Children.Count > 0;

    /// <summary>
    /// Recursively expands narrow directory paths (single-child cascade).
    /// Stops at depth 5 or when a level has more than 3 nodes.
    /// </summary>
    public static void AutoExpand(IList<ArchiveFileNode> nodes, int depth = 0)
    {
        if (depth >= 5 || nodes.Count > 3) return;
        foreach (var node in nodes)
        {
            if (!node.IsDirectory || node.Children.Count == 0) continue;
            node.IsExpanded = true;
            AutoExpand(node.Children, depth + 1);
        }
    }

    public string SizeDisplay => !IsDirectory && Size > 0 ? Localization.Strings.FormatBytes(Size) : "";
    public string ModifiedDisplay => ModifiedAt?.ToString("dd MMM yyyy, HH:mm") ?? "";

    public List<ArchiveFileNode> Children { get; } = [];

    private bool _propagating;

    /// <summary>
    /// Raised once after a user-initiated check change and all propagation is complete.
    /// Subscribe to this instead of per-node PropertyChanged for status updates.
    /// </summary>
    public static event Action? CheckTreeChanged;

    partial void OnIsCheckedChanged(bool? value)
    {
        if (_propagating) return;
        _propagating = true;

        if (value.HasValue)
            SetChildrenChecked(value.Value);

        _propagating = false;

        UpdateParent();

        CheckTreeChanged?.Invoke();
    }

    private void SetChildrenChecked(bool value)
    {
        foreach (var child in Children)
        {
            child._propagating = true;
            child.IsChecked = value;
            child.SetChildrenChecked(value);
            child._propagating = false;
        }
    }

    private void UpdateParent()
    {
        if (Parent is null || Parent._propagating) return;

        Parent._propagating = true;

        var allChecked = Parent.Children.All(c => c.IsChecked == true);
        var allUnchecked = Parent.Children.All(c => c.IsChecked == false);
        Parent.IsChecked = allChecked ? true : allUnchecked ? false : null;

        Parent._propagating = false;
        Parent.UpdateParent();
    }
}

public static class ArchiveFileTreeBuilder
{
    public static List<ArchiveFileNode> Build(string jsonLines)
    {
        var nodes = new Dictionary<string, ArchiveFileNode>(StringComparer.Ordinal);
        var rootChildren = new List<ArchiveFileNode>();

        foreach (var line in jsonLines.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;

                var path = root.TryGetProperty("path", out var pathProp) ? pathProp.GetString() : null;
                if (string.IsNullOrEmpty(path)) continue;

                var type = root.TryGetProperty("type", out var typeProp) ? typeProp.GetString() : "-";
                var size = root.TryGetProperty("size", out var sizeProp) && sizeProp.ValueKind == JsonValueKind.Number
                    ? sizeProp.GetInt64() : 0;
                DateTime? mtime = root.TryGetProperty("mtime", out var mtimeProp) && mtimeProp.ValueKind == JsonValueKind.String
                    && DateTime.TryParse(mtimeProp.GetString(), out var mt) ? mt : null;

                var isDir = type == "d";
                EnsureNode(nodes, rootChildren, path, isDir, size, mtime);
            }
            catch
            {
                // Skip malformed lines
            }
        }

        SortChildren(rootChildren);
        return rootChildren;
    }

    private static ArchiveFileNode EnsureNode(
        Dictionary<string, ArchiveFileNode> nodes,
        List<ArchiveFileNode> rootChildren,
        string path, bool isDir, long size, DateTime? mtime = null)
    {
        if (nodes.TryGetValue(path, out var existing))
            return existing;

        var slashIndex = path.LastIndexOf('/');
        var name = slashIndex >= 0 ? path[(slashIndex + 1)..] : path;

        ArchiveFileNode? parent = null;
        if (slashIndex >= 0)
        {
            var parentPath = path[..slashIndex];
            parent = EnsureNode(nodes, rootChildren, parentPath, true, 0);
        }

        var node = new ArchiveFileNode
        {
            Name = name,
            FullPath = path,
            IsDirectory = isDir,
            Size = size,
            ModifiedAt = mtime,
            Parent = parent,
            Depth = parent is not null ? parent.Depth + 1 : 0
        };
        nodes[path] = node;

        if (parent is not null)
            parent.Children.Add(node);
        else
            rootChildren.Add(node);

        return node;
    }

    private static readonly Comparison<ArchiveFileNode> NodeComparison = (a, b) =>
    {
        if (a.IsDirectory != b.IsDirectory)
            return a.IsDirectory ? -1 : 1;
        return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
    };

    private static void SortChildren(List<ArchiveFileNode> children)
    {
        children.Sort(NodeComparison);
        foreach (var child in children)
        {
            if (child.Children.Count > 0)
            {
                child.Children.Sort(NodeComparison);
                SortChildren(child.Children);
            }
        }
    }
}
