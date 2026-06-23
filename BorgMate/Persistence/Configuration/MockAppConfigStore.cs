using System;
using System.Threading;
using System.Threading.Tasks;
using BorgMate.Domain.Repositories;
using OneOf;

namespace BorgMate.Persistence.Configuration;

/// <summary>
///     Config service for demo mode — loads config but never writes to disk.
/// </summary>
public class MockAppConfigStore : ISettingsStore, IRepositoryConfigStore
{
    private readonly JsonAppConfigStore _inner = new();

    public MockAppConfigStore()
    {
        _inner.Load();
    }

    event Action<RepositoryConfigSnapshot>? IRepositoryConfigStore.Changed
    {
        add => RepositoriesChanged += value;
        remove => RepositoriesChanged -= value;
    }

    RepositoryConfigSnapshot IRepositoryConfigStore.GetSnapshot()
    {
        return ((IRepositoryConfigStore)_inner).GetSnapshot();
    }

    Task<OneOf<RepositoryConfigSnapshot, StoreError>> IRepositoryConfigStore.LoadAsync(CancellationToken ct)
    {
        return ((IRepositoryConfigStore)_inner).LoadAsync(ct);
    }

    Task<OneOf<StoreOk, StoreError>> IRepositoryConfigStore.SaveAsync(CancellationToken ct)
    {
        return Task.FromResult<OneOf<StoreOk, StoreError>>(StoreOk.Instance);
    }

    public Task<OneOf<RepositoryConfigSnapshot, StoreError>> AddAsync(BorgRepository repo,
        CancellationToken ct = default)
    {
        return _inner.AddAsync(repo, ct);
    }

    public Task<OneOf<RepositoryConfigSnapshot, StoreError>> UpdateAsync(BorgRepository repo,
        CancellationToken ct = default)
    {
        return _inner.UpdateAsync(repo, ct);
    }

    public Task<OneOf<RepositoryConfigSnapshot, StoreError>> RemoveAsync(BorgRepository repo,
        CancellationToken ct = default)
    {
        return _inner.RemoveAsync(repo, ct);
    }

    public BorgRepository? FindByPath(string path)
    {
        return _inner.FindByPath(path);
    }

    public string GenerateUniqueCopyName(string baseName)
    {
        return _inner.GenerateUniqueCopyName(baseName);
    }

    event Action<SettingsSnapshot>? ISettingsStore.Changed
    {
        add => SettingsChanged += value;
        remove => SettingsChanged -= value;
    }

    public void RequestSave()
    {
        SettingsChanged?.Invoke(((ISettingsStore)this).GetSnapshot());
        RepositoriesChanged?.Invoke(((IRepositoryConfigStore)this).GetSnapshot());
    }

    SettingsSnapshot ISettingsStore.GetSnapshot()
    {
        return ((ISettingsStore)_inner).GetSnapshot();
    }

    Task<OneOf<SettingsSnapshot, StoreError>> ISettingsStore.LoadAsync(CancellationToken ct)
    {
        return ((ISettingsStore)_inner).LoadAsync(ct);
    }

    Task<OneOf<StoreOk, StoreError>> ISettingsStore.SaveAsync(CancellationToken ct)
    {
        return Task.FromResult<OneOf<StoreOk, StoreError>>(StoreOk.Instance);
    }

    public event Action<SettingsSnapshot>? SettingsChanged;
    public event Action<RepositoryConfigSnapshot>? RepositoriesChanged;
}