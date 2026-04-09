using System;

namespace BorgMate.Services.Config;

public interface IConfigService
{
    /// <summary>
    /// Fired by RequestSave(). MainWindowViewModel subscribes and writes config to disk.
    /// </summary>
    event Action? SaveRequested;

    /// <summary>Signals that config should be saved. Does not save immediately — fires SaveRequested.</summary>
    void RequestSave();
    ConfigData Load();
    void Save(ConfigData data);
}
