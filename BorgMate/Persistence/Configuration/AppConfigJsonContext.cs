using System.Text.Json.Serialization;

namespace BorgMate.Persistence.Configuration;

[JsonSourceGenerationOptions(
    WriteIndented = true,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(AppConfigDocument))]
public partial class AppConfigJsonContext : JsonSerializerContext;