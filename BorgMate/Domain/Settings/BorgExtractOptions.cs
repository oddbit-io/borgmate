namespace BorgMate.Domain.Settings;

/// <summary>Options passed to <c>borg extract</c> when restoring archives.</summary>
public sealed class BorgExtractOptions
{
    /// <summary>Do not restore platform-specific file flags.</summary>
    public bool NoFlags { get; set; } = true;

    /// <summary>Do not restore access control lists.</summary>
    public bool NoAcls { get; set; } = true;

    /// <summary>Do not restore extended attributes.</summary>
    public bool NoXattrs { get; set; } = true;

    /// <summary>Create sparse files when extracting archive contents.</summary>
    public bool Sparse { get; set; }

    /// <summary>Creates a detached copy so restore jobs use the options selected at enqueue time.</summary>
    public BorgExtractOptions Clone()
    {
        return new BorgExtractOptions
        {
            NoFlags = NoFlags,
            NoAcls = NoAcls,
            NoXattrs = NoXattrs,
            Sparse = Sparse
        };
    }
}
