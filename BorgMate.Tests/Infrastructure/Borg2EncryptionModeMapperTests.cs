using BorgMate.Domain.Repositories;
using BorgMate.Infrastructure.Borg;

namespace BorgMate.Tests.Infrastructure;

public class Borg2EncryptionModeMapperTests
{
    [Fact]
    public void BuildRepoCreateArgs_Beta21_UsesCombinedMode()
    {
        var version = BorgCliVersion.Parse("borg 2.0.0b21");

        var args = Borg2EncryptionModeMapper.BuildRepoCreateArgs(
            Borg2EncryptionMode.RepokeyBlake2AesOcb,
            version);

        Assert.Equal("--encryption=repokey-blake2-aes-ocb", args);
    }

    [Fact]
    public void BuildRepoCreateArgs_UnknownVersion_DefaultsToBeta21CombinedMode()
    {
        var args = Borg2EncryptionModeMapper.BuildRepoCreateArgs(
            Borg2EncryptionMode.KeyfileChacha20Poly1305,
            null);

        Assert.Equal("--encryption=keyfile-chacha20-poly1305", args);
    }

    [Fact]
    public void BuildRepoCreateArgs_Beta22Dev_MapsRepokeyBlake2ChachaToSplitArgs()
    {
        var version = BorgCliVersion.Parse("borg 2.0.0b22.dev437");

        var args = Borg2EncryptionModeMapper.BuildRepoCreateArgs(
            Borg2EncryptionMode.RepokeyBlake2Chacha20Poly1305,
            version);

        Assert.Equal("--encryption=chacha20-poly1305 --id-hash=blake3 --key-location=repokey", args);
    }

    [Fact]
    public void BuildRepoCreateArgs_Beta22Dev_MapsAuthenticatedWithoutKeyLocation()
    {
        var version = BorgCliVersion.Parse("borg 2.0.0b22.dev437");

        var args = Borg2EncryptionModeMapper.BuildRepoCreateArgs(
            Borg2EncryptionMode.AuthenticatedBlake2,
            version);

        Assert.Equal("--encryption=authenticated --id-hash=blake3", args);
    }

    [Fact]
    public void BuildRepoCreateArgs_Beta22Dev_MapsNoneWithoutKeyLocation()
    {
        var version = BorgCliVersion.Parse("borg 2.0.0b22.dev437");

        var args = Borg2EncryptionModeMapper.BuildRepoCreateArgs(Borg2EncryptionMode.None, version);

        Assert.Equal("--encryption=none --id-hash=sha256", args);
    }
}
