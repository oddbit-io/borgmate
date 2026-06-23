using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using BorgMate.Domain.Archives;
using BorgMate.Domain.Repositories;
using BorgMate.Presentation.Dialogs;
using BorgMate.Runtime.Operations;
using BorgMate.Runtime.Orchestration;
using BorgMate.Runtime.Progress;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BorgMate.Presentation.ViewModels;

public partial class BrowseArchiveViewModel : ViewModelBase
{
    private readonly string? _archiveName;
    private readonly IArchiveOperationRunner? _archiveOperations;
    private readonly IFilePickerService? _filePicker;
    private readonly string? _previousArchiveName;
    private readonly BorgRepository? _repository;
    private List<ArchiveFileNode> _allNodes = [];
    private Dictionary<string, FileChangeKind>? _cachedDiff;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _hasPreviousArchive;

    [ObservableProperty]
    private bool _hasSelection;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isLoadingDiff;

    [ObservableProperty]
    private bool _isShowingChanges;

    private List<ArchiveFileNode> _rootNodes = [];

    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    private string _title = "";

    public BrowseArchiveViewModel()
    {
    }

    public BrowseArchiveViewModel(
        IArchiveOperationRunner archiveOperations,
        IFilePickerService filePicker,
        BorgRepository repository, string archiveName, string? previousArchiveName = null)
    {
        _archiveOperations = archiveOperations;
        _filePicker = filePicker;
        _repository = repository;
        _archiveName = archiveName;
        _previousArchiveName = previousArchiveName;
        HasPreviousArchive = previousArchiveName is not null;
        Title = string.Format(Strings.Get("BrowseArchiveTitle"), archiveName);
    }

    /// <summary>
    ///     Flat list of visible nodes for the virtualized ListBox.
    ///     Only contains nodes whose ancestors are all expanded.
    /// </summary>
    public ObservableCollection<ArchiveFileNode> FlatNodes { get; } = [];

    public Task<string?> PickRestoreFolderAsync()
    {
        return _filePicker?.PickFolderAsync(Strings.Get("Picker.SelectRestoreDest")) ?? Task.FromResult<string?>(null);
    }

    public void Detach()
    {
        ArchiveFileNode.CheckTreeChanged -= UpdateStatus;
        FlatNodes.Clear();
        _rootNodes = [];
        _allNodes = [];
        _cachedDiff = null;
    }

    /// <summary>
    ///     Toggles expand/collapse for a directory node. On expand, cascades through
    ///     single-child directories and inserts visible descendants into FlatNodes.
    /// </summary>
    public void ToggleExpand(ArchiveFileNode node)
    {
        if (!node.CanExpand) return;

        var index = FlatNodes.IndexOf(node);
        if (index < 0) return;

        if (node.IsExpanded)
        {
            // Collapse: remove all visible descendants
            node.IsExpanded = false;
            var removeCount = CountVisibleDescendants(index);
            for (var i = 0; i < removeCount; i++)
                FlatNodes.RemoveAt(index + 1);
        }
        else
        {
            // Expand with single-child cascade
            ExpandWithCascade(node);

            var toInsert = new List<ArchiveFileNode>();
            CollectVisible(node.Children, toInsert);
            for (var i = 0; i < toInsert.Count; i++)
                FlatNodes.Insert(index + 1 + i, toInsert[i]);
        }
    }

    private static void ExpandWithCascade(ArchiveFileNode node)
    {
        node.IsExpanded = true;
        if (node.Children.Count == 1 && node.Children[0] is { IsDirectory: true, Children.Count: > 0 } child)
            ExpandWithCascade(child);
    }

    private int CountVisibleDescendants(int index)
    {
        var depth = FlatNodes[index].Depth;
        var count = 0;
        for (var i = index + 1; i < FlatNodes.Count; i++)
        {
            if (FlatNodes[i].Depth <= depth) break;
            count++;
        }

        return count;
    }

    private static void CollectVisible(IList<ArchiveFileNode> nodes, List<ArchiveFileNode> result)
    {
        foreach (var node in nodes)
        {
            result.Add(node);
            if (node.IsExpanded && node.Children.Count > 0)
                CollectVisible(node.Children, result);
        }
    }

    private void BuildFlatList()
    {
        var flat = new List<ArchiveFileNode>();
        CollectVisible(_rootNodes, flat);
        FlatNodes.ReplaceWith(flat);
    }

    [RelayCommand]
    private async Task LoadContents()
    {
        if (_archiveOperations is null || _repository is null || _archiveName is null)
            return;

        IsLoading = true;
        ErrorMessage = null;
        StatusText = Strings.Get("LoadingContents");

        try
        {
            var result = await _archiveOperations.ListArchiveContentsAsync(
                new ArchiveContentsOperationOptions(
                    _repository,
                    NullOperationProgressSink.Instance,
                    _archiveName));

            if (result.IsT0)
            {
                _rootNodes = result.AsT0.RootNodes.ToList();
                var allNodes = CollectAllNodes(_rootNodes);
                _allNodes = allNodes;

                ArchiveFileNode.AutoExpand(_rootNodes);
                BuildFlatList();
                ArchiveFileNode.CheckTreeChanged -= UpdateStatus;
                ArchiveFileNode.CheckTreeChanged += UpdateStatus;

                UpdateStatus();
            }
            else if (result.AsT1 is not OperationError.Cancelled)
            {
                ErrorMessage = result.AsT1.ToDisplayMessage();
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task ToggleShowChanges()
    {
        if (IsShowingChanges)
        {
            foreach (var node in _allNodes)
                node.ChangeKind = FileChangeKind.None;
            IsShowingChanges = false;
            return;
        }

        if (_archiveOperations is null || _repository is null
                                       || _archiveName is null || _previousArchiveName is null)
            return;

        try
        {
            _cachedDiff ??= await LoadDiffAsync();
            if (_cachedDiff is null) return;

            ApplyDiffMarkers(_cachedDiff);
            IsShowingChanges = true;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    /// <summary>
    ///     Loads diff between previous and current archive, using cache when available.
    ///     Sets IsLoadingDiff while running.
    /// </summary>
    private async Task<Dictionary<string, FileChangeKind>?> LoadDiffAsync()
    {
        IsLoadingDiff = true;
        try
        {
            var result = await _archiveOperations!.GetArchiveDiffAsync(
                new ArchiveDiffOperationOptions(
                    _repository!,
                    NullOperationProgressSink.Instance,
                    _previousArchiveName!,
                    _archiveName!));
            if (result.IsT0)
                return result.AsT0.ChangedPaths.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

            if (result.AsT1 is not OperationError.Cancelled)
                ErrorMessage = result.AsT1.ToDisplayMessage();
            return null;
        }
        finally
        {
            IsLoadingDiff = false;
        }
    }

    /// <summary>
    ///     Sets FileChangeKind on each node from the diff results.
    ///     Propagates Modified status up to parent directories.
    /// </summary>
    private void ApplyDiffMarkers(Dictionary<string, FileChangeKind> changedPaths)
    {
        foreach (var node in _allNodes)
            if (changedPaths.TryGetValue(node.FullPath, out var kind))
                node.ChangeKind = kind;
            else
                node.ChangeKind = FileChangeKind.None;

        // Propagate to parent directories
        foreach (var node in _allNodes.Where(n => !n.IsDirectory && n.ChangeKind != FileChangeKind.None))
        {
            var parent = node.Parent;
            while (parent is not null && parent.ChangeKind == FileChangeKind.None)
            {
                parent.ChangeKind = FileChangeKind.Modified;
                parent = parent.Parent;
            }
        }
    }

    private void UpdateStatus()
    {
        var selectedFiles = _allNodes.Where(n => n.IsChecked == true && !n.IsDirectory).ToList();
        HasSelection = selectedFiles.Count > 0;

        if (!HasSelection)
        {
            StatusText = Strings.Get("NoFilesSelected");
            return;
        }

        var totalSize = selectedFiles.Sum(n => n.Size);
        StatusText = string.Format(Strings.Get("SelectedFilesWithSize"),
            selectedFiles.Count.ToString("N0", Strings.Culture), Strings.FormatBytes(totalSize));
    }

    public List<string> GetSelectedPaths()
    {
        var paths = new List<string>();
        CollectSelectedPaths(_rootNodes, paths);
        return paths;
    }

    public long GetSelectedSize()
    {
        return _allNodes.Where(n => n.IsChecked == true && !n.IsDirectory).Sum(n => n.Size);
    }

    private static void CollectSelectedPaths(IEnumerable<ArchiveFileNode> nodes, List<string> paths)
    {
        foreach (var node in nodes)
            if (node.IsChecked == true)
                paths.Add(node.FullPath);
            else if (node.IsChecked == null)
                CollectSelectedPaths(node.Children, paths);
    }

    private static List<ArchiveFileNode> CollectAllNodes(List<ArchiveFileNode> roots)
    {
        var all = new List<ArchiveFileNode>();

        void Walk(IEnumerable<ArchiveFileNode> nodes)
        {
            foreach (var n in nodes)
            {
                all.Add(n);
                Walk(n.Children);
            }
        }

        Walk(roots);
        return all;
    }
}