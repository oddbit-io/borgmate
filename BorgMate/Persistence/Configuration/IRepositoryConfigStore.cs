using System;
using System.Threading;
using System.Threading.Tasks;
using BorgMate.Domain.Repositories;
using OneOf;

namespace BorgMate.Persistence.Configuration;

public interface IRepositoryConfigStore
{
    event Action<RepositoryConfigSnapshot>? Changed;

    RepositoryConfigSnapshot GetSnapshot();
    Task<OneOf<RepositoryConfigSnapshot, StoreError>> LoadAsync(CancellationToken ct = default);
    Task<OneOf<StoreOk, StoreError>> SaveAsync(CancellationToken ct = default);
    Task<OneOf<RepositoryConfigSnapshot, StoreError>> AddAsync(BorgRepository repo, CancellationToken ct = default);
    Task<OneOf<RepositoryConfigSnapshot, StoreError>> UpdateAsync(BorgRepository repo, CancellationToken ct = default);
    Task<OneOf<RepositoryConfigSnapshot, StoreError>> RemoveAsync(BorgRepository repo, CancellationToken ct = default);

    BorgRepository? FindByPath(string path);
    string GenerateUniqueCopyName(string baseName);
}