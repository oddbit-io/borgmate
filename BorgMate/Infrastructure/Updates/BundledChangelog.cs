using System;
using System.Collections.Generic;
using System.IO;

namespace BorgMate.Infrastructure.Updates;

/// <summary>Reads the changelog compiled into the app.</summary>
public static class BundledChangelog
{
    private const string ChangelogResource = "BorgMate.Resources.Updates.changelog-v2.json";

    public static List<ChangelogEntry> Load()
    {
        using var stream = typeof(BundledChangelog).Assembly.GetManifestResourceStream(ChangelogResource)
                           ?? throw new InvalidOperationException($"Missing embedded resource {ChangelogResource}.");
        using var reader = new StreamReader(stream);
        return ChangelogParser.Parse(reader.ReadToEnd());
    }
}
