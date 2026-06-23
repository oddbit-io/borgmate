using System;
using System.Threading;
using System.Threading.Tasks;
using OneOf;

namespace BorgMate.Persistence.Configuration;

public interface ISettingsStore
{
    event Action<SettingsSnapshot>? Changed;

    SettingsSnapshot GetSnapshot();
    Task<OneOf<SettingsSnapshot, StoreError>> LoadAsync(CancellationToken ct = default);
    Task<OneOf<StoreOk, StoreError>> SaveAsync(CancellationToken ct = default);
    void RequestSave();
}