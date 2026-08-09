using BorgMate.Domain.Repositories;
using BorgMate.Infrastructure.Borg;

namespace BorgMate.Tests.Infrastructure;

public class Borg2EncryptionModeMapperTests
{
    [Fact]
    public void BuildRepoCreateArgs_UsesBeta21CombinedMode()
    {
        var args = Borg2EncryptionModeMapper.BuildRepoCreateArgs(
            Borg2EncryptionMode.RepokeyBlake2AesOcb);

        Assert.Equal("--encryption=repokey-blake2-aes-ocb", args);
    }
}
