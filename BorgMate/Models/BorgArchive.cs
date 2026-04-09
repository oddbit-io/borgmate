using System;
using BorgMate.Localization;

namespace BorgMate.Models;

/// <param name="Name">Archive name as reported by borg.</param>
/// <param name="Date">Archive creation timestamp. DateTime.MinValue when unavailable.</param>
public record BorgArchive(string Name, DateTime Date)
{
    public string DateDisplay => Date == DateTime.MinValue ? "" : Date.ToString("d MMMM yyyy, HH:mm", Strings.Culture);
}
