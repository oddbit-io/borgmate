namespace BorgMate.Tests.Infrastructure;

/// <summary>
///     Release files made with gpg. One test release key has a current and a next signing subkey;
///     <c>release-keys-{current,next,both}.asc</c> export it with those subkeys, and
///     <c>manifest.json.{current,next,both}.asc</c> are signed by them.
///     <c>manifest.json.foreign.asc</c> is signed by an unrelated key.
///     <c>release-keys-lifecycle.asc</c> was made with BouncyCastle: a 2019 release key with a valid, a
///     revoked and a one-day signing subkey. <c>manifest.json.lifecycle-{subkey,primary,revoked,expired}.asc</c>
///     are signed by that key on 2019-01-01 12:00 UTC, and <c>-future</c> by the valid subkey dated 2099.
/// </summary>
public static class UpdateFixtures
{
    public static byte[] Read(string name)
    {
        using var stream = typeof(UpdateFixtures).Assembly.GetManifestResourceStream(
                               $"BorgMate.Tests.Infrastructure.Fixtures.Updates.{name}")
                           ?? throw new InvalidOperationException($"Missing update fixture {name}.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
