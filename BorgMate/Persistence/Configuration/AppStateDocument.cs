using System;
using System.Collections.Generic;

namespace BorgMate.Persistence.Configuration;

public class AppStateDocument
{
    public double? WindowX { get; set; }
    public double? WindowY { get; set; }
    public double? WindowWidth { get; set; }
    public double? WindowHeight { get; set; }
    public bool IsMaximized { get; set; }
    public bool SidebarExpanded { get; set; }
    public Dictionary<string, DateTime> LastBackupAtByRepositoryPath { get; set; } = [];
}
