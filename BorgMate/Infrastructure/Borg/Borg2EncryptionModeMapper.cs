using BorgMate.Domain.Repositories;

namespace BorgMate.Infrastructure.Borg;

/// <summary>Maps BorgMate Borg 2 encryption choices to version-specific repo-create flags.</summary>
public static class Borg2EncryptionModeMapper
{
    /// <summary>Builds the <c>borg repo-create</c> encryption arguments for the detected Borg 2 CLI.</summary>
    public static string BuildRepoCreateArgs(Borg2EncryptionMode mode, BorgCliVersion? version)
    {
        return version is { UsesBorg2SplitEncryption: true }
            ? BuildSplitArgs(mode)
            : $"--encryption={ToBorg2Beta21String(mode)}";
    }

    /// <summary>Returns the Borg 2.0.0b21 combined encryption mode name.</summary>
    public static string ToBorg2Beta21String(Borg2EncryptionMode mode)
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

    private static string BuildSplitArgs(Borg2EncryptionMode mode)
    {
        var encryption = mode switch
        {
            Borg2EncryptionMode.None => "none",
            Borg2EncryptionMode.Authenticated or Borg2EncryptionMode.AuthenticatedBlake2 => "authenticated",
            Borg2EncryptionMode.RepokeyChacha20Poly1305
                or Borg2EncryptionMode.RepokeyBlake2Chacha20Poly1305
                or Borg2EncryptionMode.KeyfileChacha20Poly1305
                or Borg2EncryptionMode.KeyfileBlake2Chacha20Poly1305 => "chacha20-poly1305",
            _ => "aes256-ocb"
        };

        var idHash = mode is Borg2EncryptionMode.AuthenticatedBlake2
            or Borg2EncryptionMode.RepokeyBlake2AesOcb
            or Borg2EncryptionMode.RepokeyBlake2Chacha20Poly1305
            or Borg2EncryptionMode.KeyfileBlake2AesOcb
            or Borg2EncryptionMode.KeyfileBlake2Chacha20Poly1305
                ? "blake3"
                : "sha256";

        if (mode == Borg2EncryptionMode.None)
            return "--encryption=none --id-hash=sha256";

        var keyLocation = mode switch
        {
            Borg2EncryptionMode.KeyfileAesOcb
                or Borg2EncryptionMode.KeyfileChacha20Poly1305
                or Borg2EncryptionMode.KeyfileBlake2AesOcb
                or Borg2EncryptionMode.KeyfileBlake2Chacha20Poly1305 => "keyfile",
            Borg2EncryptionMode.Authenticated or Borg2EncryptionMode.AuthenticatedBlake2 => null,
            _ => "repokey"
        };

        return keyLocation is null
            ? $"--encryption={encryption} --id-hash={idHash}"
            : $"--encryption={encryption} --id-hash={idHash} --key-location={keyLocation}";
    }
}
