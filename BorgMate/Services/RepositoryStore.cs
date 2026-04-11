using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Avalonia.Threading;
using BorgMate.Models;
using BorgMate.Services.Borg;
using BorgMate.Services.Queue;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BorgMate.Services;

/// <summary>Single source of truth for repos, selection, per-repo loading state, and error rollup.</summary>
public sealed partial class RepositoryStore : ObservableObject, IDisposable
{
    private readonly JobQueueService _jobQueue;
    private readonly HashSet<string> _loadingArchives = new();

    public ObservableCollection<BorgRepository> Repositories { get; } = [];

    [ObservableProperty]
    private BorgRepository? _selectedRepository;

    /// <summary>Fired with (oldValue, newValue) on selection change.</summary>
    public event Action<BorgRepository?, BorgRepository?>? SelectionChanged;

    /// <summary>Fired when the selected repo's archive loading state changes.</summary>
    public event Action? SelectedLoadingStateChanged;

    public RepositoryStore(JobQueueService jobQueue)
    {
        _jobQueue = jobQueue;
        _jobQueue.JobCompleted += OnJobCompleted;
    }

    partial void OnSelectedRepositoryChanging(BorgRepository? oldValue, BorgRepository? newValue)
    {
        if (oldValue != newValue)
            SelectionChanged?.Invoke(oldValue, newValue);
    }

    public void Add(BorgRepository repo) => Repositories.Add(repo);

    public void Remove(BorgRepository repo)
    {
        Repositories.Remove(repo);
        _loadingArchives.Remove(repo.Path);
        if (SelectedRepository == repo)
            SelectedRepository = null;
    }

    public BorgRepository? FindByPath(string path)
    {
        foreach (var r in Repositories)
            if (r.Path == path) return r;
        return null;
    }

    // --- Per-repo archive loading state ---

    public bool IsLoadingArchives(BorgRepository repo) => _loadingArchives.Contains(repo.Path);

    public void SetLoadingArchives(BorgRepository repo, bool value)
    {
        var changed = value ? _loadingArchives.Add(repo.Path) : _loadingArchives.Remove(repo.Path);
        if (changed && SelectedRepository == repo)
            SelectedLoadingStateChanged?.Invoke();
    }

    // --- HasError rollup ---

    private void OnJobCompleted(BorgJob job, BorgResult result)
    {
        if (Avalonia.Application.Current is null || Dispatcher.UIThread.CheckAccess())
            HandleJobCompleted(job, result);
        else
            Dispatcher.UIThread.Post(() => HandleJobCompleted(job, result));
    }

    internal void HandleJobCompleted(BorgJob job, BorgResult result)
    {
        if (job.RepoPath is null) return;
        if (result.WasCancelled) return;

        var repo = FindByPath(job.RepoPath);
        if (repo is null) return;

        if (result.Success)
        {
            repo.HasError = false;
            repo.LastError = null;
        }
        else
        {
            repo.HasError = true;
            repo.LastError = result.ErrorMessage;
        }
    }

    public void Dispose()
    {
        _jobQueue.JobCompleted -= OnJobCompleted;
    }
}
