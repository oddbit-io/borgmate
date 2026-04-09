using System;
using BorgMate.Services.Config;

namespace BorgMate.Services.Mocks;

/// <summary>
/// Config service for demo mode — loads config but never writes to disk.
/// </summary>
public class MockConfigService : IConfigService
{
    private readonly ConfigService _inner = new();

    public event Action? SaveRequested;
    public void RequestSave() => SaveRequested?.Invoke();
    public ConfigData Load() => _inner.Load();
    public void Save(ConfigData data) { }
}
