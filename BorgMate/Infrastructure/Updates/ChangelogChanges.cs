using System.Collections.Generic;

namespace BorgMate.Infrastructure.Updates;

public class ChangelogChanges
{
    public List<string> Features { get; init; } = [];

    public List<string> Improvements { get; init; } = [];

    public List<string> Fixes { get; init; } = [];

    public List<string> Security { get; init; } = [];
}
