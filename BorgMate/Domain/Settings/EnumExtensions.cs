using BorgMate.Domain.Repositories;

namespace BorgMate.Domain.Settings;

public static class EnumExtensions
{
    private static readonly string[] BorgEncryptionModeStrings =
        ["none", "repokey", "repokey-blake2", "keyfile", "keyfile-blake2", "authenticated", "authenticated-blake2"];

    public static string ToBorgString(this BorgEncryptionMode mode)
    {
        return (int)mode < BorgEncryptionModeStrings.Length ? BorgEncryptionModeStrings[(int)mode] : "repokey-blake2";
    }

    public static string ToBorgString(this Borg2EncryptionMode mode)
    {
        return mode switch
        {
            Borg2EncryptionMode.None => "none",
            Borg2EncryptionMode.Authenticated => "authenticated",
            Borg2EncryptionMode.AuthenticatedBlake2 => "authenticated-blake2",
            Borg2EncryptionMode.RepokeyAesOcb => "repokey-aes-ocb",
            Borg2EncryptionMode.RepokeyChacha20Poly1305 => "repokey-chacha20-poly1305",
            Borg2EncryptionMode.RepokeyBlake2AesOcb => "repokey-blake2-aes-ocb",
            Borg2EncryptionMode.RepokeyBlake2Chacha20Poly1305 => "repokey-blake2-chacha20-poly1305",
            Borg2EncryptionMode.KeyfileAesOcb => "keyfile-aes-ocb",
            Borg2EncryptionMode.KeyfileChacha20Poly1305 => "keyfile-chacha20-poly1305",
            Borg2EncryptionMode.KeyfileBlake2AesOcb => "keyfile-blake2-aes-ocb",
            Borg2EncryptionMode.KeyfileBlake2Chacha20Poly1305 => "keyfile-blake2-chacha20-poly1305",
            _ => "repokey-aes-ocb"
        };
    }
}
