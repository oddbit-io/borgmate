namespace BorgMate.Models;

public static class EnumExtensions
{
    private static readonly string[] BorgEncryptionModeStrings =
        ["none", "repokey", "repokey-blake2", "keyfile", "keyfile-blake2", "authenticated", "authenticated-blake2"];

    public static string ToBorgString(this BorgEncryptionMode mode) =>
        (int)mode < BorgEncryptionModeStrings.Length ? BorgEncryptionModeStrings[(int)mode] : "repokey-blake2";
}
