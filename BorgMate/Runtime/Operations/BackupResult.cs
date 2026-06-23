using System;

namespace BorgMate.Runtime.Operations;

/// <summary>Successful backup operation result.</summary>
public sealed record BackupResult(string ArchiveName, DateTimeOffset CompletedAt);