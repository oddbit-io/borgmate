using System.Text.Json.Serialization;

namespace BorgMate.Domain.Repositories;

/// <summary>How a repository location is represented in BorgMate.</summary>
public enum BorgRepositoryLocationKind
{
    [JsonStringEnumMemberName("localPath")]
    LocalPath,

    [JsonStringEnumMemberName("sshFields")]
    SshFields,

    [JsonStringEnumMemberName("borgUrl")]
    BorgUrl,

    [JsonStringEnumMemberName("smbShare")]
    SmbShare
}
