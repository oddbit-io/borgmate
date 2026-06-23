using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using BorgMate.Domain.Repositories;
using BorgMate.Presentation.ViewModels;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BorgMate.Presentation.State;

/// <summary>Single source of truth for repositories, selection, and per-repository loading state.</summary>
public sealed partial class RepositoryPresentationState : ObservableObject
{
    private readonly HashSet<string> _loadingArchives = new();

    [ObservableProperty]
    private RepositoryViewModel? _selectedRepository;

    public ObservableCollection<RepositoryViewModel> Repositories { get; } = [];

    /// <summary>Fired with (oldValue, newValue) on selection change.</summary>
    public event Action<RepositoryViewModel?, RepositoryViewModel?>? SelectionChanged;

    /// <summary>Fired when the selected repo's archive loading state changes.</summary>
    public event Action? SelectedLoadingStateChanged;

    partial void OnSelectedRepositoryChanging(RepositoryViewModel? oldValue, RepositoryViewModel? newValue)
    {
        if (oldValue != newValue)
            SelectionChanged?.Invoke(oldValue, newValue);
    }

    public void Add(RepositoryViewModel repo)
    {
        Repositories.Add(repo);
    }

    public void Add(BorgRepository repo)
    {
        Repositories.Add(new RepositoryViewModel(repo));
    }

    public void Remove(RepositoryViewModel repo)
    {
        Repositories.Remove(repo);
        _loadingArchives.Remove(repo.Path);
        if (SelectedRepository == repo)
            SelectedRepository = null;
    }

    public RepositoryViewModel? FindByPath(string path)
    {
        foreach (var r in Repositories)
            if (r.Path == path)
                return r;
        return null;
    }

    // --- Per-repo archive loading state ---

    public bool IsLoadingArchives(RepositoryViewModel repo)
    {
        return _loadingArchives.Contains(repo.Path);
    }

    public void SetLoadingArchives(RepositoryViewModel repo, bool value)
    {
        var changed = value ? _loadingArchives.Add(repo.Path) : _loadingArchives.Remove(repo.Path);
        if (changed && SelectedRepository == repo)
            SelectedLoadingStateChanged?.Invoke();
    }
}