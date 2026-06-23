using BorgMate.Domain.Settings;

namespace BorgMate.Persistence.Configuration;

public sealed record SettingsSnapshot(AppSettings Settings);