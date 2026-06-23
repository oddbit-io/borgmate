using System;
using System.Collections.Generic;

namespace BorgMate.Runtime.Operations;

/// <summary>Successful restore operation result.</summary>
public sealed record RestoreResult(
    string ArchiveName,
    string DestinationPath,
    IReadOnlyList<string>? RestoredPaths,
    DateTimeOffset CompletedAt);