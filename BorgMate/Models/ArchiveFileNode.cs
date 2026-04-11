using System;
using System.Collections.Generic;
using System.Linq;
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
