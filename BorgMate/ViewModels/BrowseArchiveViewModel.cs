using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using BorgMate.Localization;
using BorgMate.Models;
using BorgMate.Services;
using BorgMate.Services.Borg;
using BorgMate.Services.Queue;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BorgMate.ViewModels;

public partial class BrowseArchiveViewModel : ViewModelBase
{
    private readonly BorgServiceFactory? _borgServiceFactory;
    private readonly JobQueueService? _jobQueue;
    private readonly BorgCacheService? _cache;
    private readonly IFilePickerService? _filePicker;
    private readonly BorgOperationRunner? _runner;
    private readonly BorgRepository? _repository;
    private readonly string? _archiveName;
    private readonly string? _previousArchiveName;
    private List<ArchiveFileNode> _rootNodes = [];
    private List<ArchiveFileNode> _allNodes = [];
    private Dictionary<string, FileChangeKind>? _cachedDiff;

    public BrowseArchiveViewModel() { }

    public BrowseArchiveViewModel(BorgServiceFactory borgServiceFactory, JobQueueService jobQueue,
        BorgCacheService cache, IFilePickerService filePicker, BorgOperationRunner runner,
        BorgRepository repository, string archiveName, string? previousArchiveName = null)
    {
        _borgServiceFactory = borgServiceFactory;
        _jobQueue = jobQueue;
        _cache = cache;
        _filePicker = filePicker;
        _runner = runner;
        _repository = repository;
        _archiveName = archiveName;
        _previousArchiveName = previousArchiveName;
        HasPreviousArchive = previousArchiveName is not null;
        Title = string.Format(Strings.Get("BrowseArchiveTitle"), archiveName);
    }

    public Task<string?> PickRestoreFolderAsync() =>
        _filePicker?.PickFolderAsync(Strings.Get("Picker.SelectRestoreDest")) ?? Task.FromResult<string?>(null);

    [ObservableProperty]
    private string _title = "";

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    private bool _hasSelection;

    [ObservableProperty]
    private bool _hasPreviousArchive;

    [ObservableProperty]
    private bool _isShowingChanges;

    [ObservableProperty]
    private bool _isLoadingDiff;

    /// <summary>
    /// Flat list of visible nodes for the virtualized ListBox.
    /// Only contains nodes whose ancestors are all expanded.
    /// </summary>
    public ObservableCollection<ArchiveFileNode> FlatNodes { get; } = [];

    public void Detach()
    {
        ArchiveFileNode.CheckTreeChanged -= UpdateStatus;
        FlatNodes.Clear();
        _rootNodes = [];
        _allNodes = [];
        _cachedDiff = null;
    }

    /// <summary>
    /// Toggles expand/collapse for a directory node. On expand, cascades through
    /// single-child directories and inserts visible descendants into FlatNodes.
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
        if (_borgServiceFactory is null || _jobQueue is null || _repository is null || _archiveName is null)
            return;

        IsLoading = true;
        ErrorMessage = null;
        StatusText = Strings.Get("LoadingContents");

        try
        {
            var stdout = _cache?.GetArchiveContents(_repository.Path, _archiveName);
            if (stdout is null)
            {
                var service = _borgServiceFactory.GetService(_repository.BorgVersion);
                var job = _jobQueue.Enqueue(
                    $"{Strings.Get("Job.ListContents")}: {_repository.Name}::{_archiveName}",
                    async (j, ct, progress) =>
                    {
                        progress.Report(Strings.Get("LoadingContents"));
                        return await _runner!.RunWithPassphraseRetry(
                            _repository, () => _runner!.RunWithTransientRetry(j,
                                () => service.ListArchiveContentsAsync(_repository, _archiveName, ct)));
                    },
                    BorgJobKind.Query, $"contents:{_repository.Path}::{_archiveName}", _repository.Path);

                var result = await job.Completion.Task;

                if (result.Success)
                {
                    stdout = result.StandardOutput;
                    _cache?.SetArchiveContents(_repository.Path, _archiveName, stdout);
                }
                else if (!result.WasCancelled)
                {
                    ErrorMessage = result.ErrorMessage;
                }
            }

            if (stdout is not null)
            {
                var (roots, allNodes) = await Task.Run(() =>
                {
                    var r = ArchiveFileTreeBuilder.Build(stdout);
                    var all = CollectAllNodes(r);
                    return (r, all);
                });
                _rootNodes = roots;
                _allNodes = allNodes;

                ArchiveFileNode.AutoExpand(_rootNodes);
                BuildFlatList();
                ArchiveFileNode.CheckTreeChanged -= UpdateStatus;
                ArchiveFileNode.CheckTreeChanged += UpdateStatus;

                UpdateStatus();
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

        if (_borgServiceFactory is null || _jobQueue is null || _repository is null
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
    /// Loads diff between previous and current archive, using cache when available.
    /// Sets IsLoadingDiff while running.
    /// </summary>
    private async Task<Dictionary<string, FileChangeKind>?> LoadDiffAsync()
    {
        var cached = _cache?.GetDiff(_repository!.Path, _previousArchiveName!, _archiveName!);
        if (cached is not null) return cached;

        IsLoadingDiff = true;
        try
        {
            var service = _borgServiceFactory!.GetService(_repository!.BorgVersion);
            var diffTag = $"{_repository.Path}::{_previousArchiveName}..{_archiveName}";
            var job = _jobQueue!.Enqueue(
                $"{Strings.Get("Job.Diff")}: {_repository.Name}::{_archiveName}",
                async (j, ct, progress) =>
                {
                    progress.Report(Strings.Get("BrowseArchive.DiffLoading"));
                    return await _runner!.RunWithPassphraseRetry(
                        _repository, () => _runner!.RunWithTransientRetry(j,
                            () => service.DiffArchivesAsync(_repository, _previousArchiveName!, _archiveName!, ct)));
                },
                BorgJobKind.Query, $"diff:{diffTag}", _repository.Path);

            var result = await job.Completion.Task;
            if (result.Success)
            {
                var diff = await Task.Run(() => BorgDiffParser.ParseChangedPaths(result.StandardOutput));
                _cache?.SetDiff(_repository.Path, _previousArchiveName!, _archiveName!, diff);
                return diff;
            }

            if (!result.WasCancelled)
                ErrorMessage = result.ErrorMessage;
            return null;
        }
        finally
        {
            IsLoadingDiff = false;
        }
    }

    /// <summary>
    /// Sets FileChangeKind on each node from the diff results.
    /// Propagates Modified status up to parent directories.
    /// </summary>
    private void ApplyDiffMarkers(Dictionary<string, FileChangeKind> changedPaths)
    {
        foreach (var node in _allNodes)
        {
            if (changedPaths.TryGetValue(node.FullPath, out var kind))
                node.ChangeKind = kind;
            else
                node.ChangeKind = FileChangeKind.None;
        }

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

    public long GetSelectedSize() =>
        _allNodes.Where(n => n.IsChecked == true && !n.IsDirectory).Sum(n => n.Size);

    private static void CollectSelectedPaths(IEnumerable<ArchiveFileNode> nodes, List<string> paths)
    {
        foreach (var node in nodes)
        {
            if (node.IsChecked == true)
                paths.Add(node.FullPath);
            else if (node.IsChecked == null)
                CollectSelectedPaths(node.Children, paths);
        }
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
