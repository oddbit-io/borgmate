using System;
using System.Collections.Generic;
using System.Text.Json;
using BorgMate.Models;

namespace BorgMate.Services.Borg;

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
