using System.Collections.Generic;
using BorgMate.Domain.Archives;

namespace BorgMate.Runtime.Operations;

/// <summary>Successful archive diff operation result.</summary>
public sealed record ArchiveDiffResult(IReadOnlyDictionary<string, FileChangeKind> ChangedPaths);