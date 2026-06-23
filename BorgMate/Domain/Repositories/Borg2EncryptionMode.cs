using System.Text.Json.Serialization;

namespace BorgMate.Domain.Repositories;

/// <summary>Borg 2 repository encryption modes for the 2.0.0b21 CLI.</summary>
public enum Borg2EncryptionMode
{
    [JsonStringEnumMemberName("none")]
    None,

    [JsonStringEnumMemberName("authenticated")]
    Authenticated,

    [JsonStringEnumMemberName("authenticated-blake2")]
    AuthenticatedBlake2,

    [JsonStringEnumMemberName("repokey-aes-ocb")]
    RepokeyAesOcb,

    [JsonStringEnumMemberName("repokey-chacha20-poly1305")]
    RepokeyChacha20Poly1305,

    [JsonStringEnumMemberName("repokey-blake2-aes-ocb")]
    RepokeyBlake2AesOcb,

    [JsonStringEnumMemberName("repokey-blake2-chacha20-poly1305")]
    RepokeyBlake2Chacha20Poly1305,

    [JsonStringEnumMemberName("keyfile-aes-ocb")]
    KeyfileAesOcb,

    [JsonStringEnumMemberName("keyfile-chacha20-poly1305")]
    KeyfileChacha20Poly1305,

    [JsonStringEnumMemberName("keyfile-blake2-aes-ocb")]
    KeyfileBlake2AesOcb,

    [JsonStringEnumMemberName("keyfile-blake2-chacha20-poly1305")]
    KeyfileBlake2Chacha20Poly1305
}
