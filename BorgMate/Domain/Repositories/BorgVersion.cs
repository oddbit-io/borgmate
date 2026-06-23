using System.Text.Json.Serialization;

namespace BorgMate.Domain.Repositories;

public enum BorgVersion
{
    [JsonStringEnumMemberName("borg1")]
    Borg1,

    [JsonStringEnumMemberName("borg2")]
    Borg2
}