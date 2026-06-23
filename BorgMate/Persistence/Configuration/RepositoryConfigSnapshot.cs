using System.Collections.Generic;
using BorgMate.Domain.Repositories;

namespace BorgMate.Persistence.Configuration;

public sealed record RepositoryConfigSnapshot(IReadOnlyList<BorgRepository> Repositories);