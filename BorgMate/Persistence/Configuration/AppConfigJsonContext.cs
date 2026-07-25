using System.Text.Json.Serialization;

namespace BorgMate.Persistence.Configuration;

[JsonSourceGenerationOptions(
    WriteIndented = true,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(AppConfigDocument))]
[JsonSerializable(typeof(AppStateDocument))]
public partial class AppConfigJsonContext : JsonSerializerContext;
