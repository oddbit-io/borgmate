using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BorgMate.Models;

/// <param name="name">Archive name as reported by borg.</param>
/// <param name="date">Archive creation timestamp. DateTime.MinValue when unavailable.</param>
public partial class BorgArchive(string name, DateTime date) : ObservableObject
{
    public string Name { get; } = name;
    public DateTime Date { get; } = date;

    /// <summary>Original (uncompressed) size in bytes. Null until borg info is fetched.</summary>
    public long? OriginalSize { get; set; }

    /// <summary>Number of files in the archive. Null until borg info is fetched.</summary>
    public long? FileCount { get; set; }

    [ObservableProperty]
    private bool _isLoadingDetail;

    public bool HasDetail => OriginalSize is not null;
}
