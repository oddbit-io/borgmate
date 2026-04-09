using System.Text.Json.Serialization;

namespace BorgMate.Services.Config;

[JsonSourceGenerationOptions(
    WriteIndented = true,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(ConfigData))]
public partial class ConfigJsonContext : JsonSerializerContext;
