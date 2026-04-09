using System.Text.Json.Serialization;

namespace BorgMate.Models;

public enum BorgEncryptionMode
{
    [JsonStringEnumMemberName("none")] None,
    [JsonStringEnumMemberName("repokey")] Repokey,
    [JsonStringEnumMemberName("repokey-blake2")] RepokeyBlake2,
    [JsonStringEnumMemberName("keyfile")] Keyfile,
    [JsonStringEnumMemberName("keyfile-blake2")] KeyfileBlake2,
    [JsonStringEnumMemberName("authenticated")] Authenticated,
    [JsonStringEnumMemberName("authenticated-blake2")] AuthenticatedBlake2
}
