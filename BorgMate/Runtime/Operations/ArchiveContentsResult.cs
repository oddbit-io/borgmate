using System.Collections.Generic;
using ArchiveFileNode = BorgMate.Domain.Archives.ArchiveFileNode;

namespace BorgMate.Runtime.Operations;

/// <summary>Successful archive contents operation result.</summary>
public sealed record ArchiveContentsResult(IReadOnlyList<ArchiveFileNode> RootNodes, string RawOutput);