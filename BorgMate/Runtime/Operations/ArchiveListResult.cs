using System.Collections.Generic;
using BorgArchive = BorgMate.Domain.Archives.BorgArchive;

namespace BorgMate.Runtime.Operations;

/// <summary>Successful archive list operation result.</summary>
public sealed record ArchiveListResult(IReadOnlyList<BorgArchive> Archives);