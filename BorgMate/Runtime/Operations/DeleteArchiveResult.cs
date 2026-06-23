using System;

namespace BorgMate.Runtime.Operations;

/// <summary>Successful archive delete operation result.</summary>
public sealed record DeleteArchiveResult(string ArchiveName, DateTimeOffset CompletedAt);