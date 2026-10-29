namespace BorgMate.Domain.Repositories;

/// <summary>
///     A repository location change reported by Borg's relocation check, in Borg's canonical
///     location form. Borg reports the same pair again when a location alternates, such as an
///     SMB share mounted under a different mount point name.
/// </summary>
public record RepositoryRelocation
{
    public required string Location { get; init; }

    public required string PreviousLocation { get; init; }
}
