using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Win32;

namespace BorgMate.Infrastructure.Platform;

[SuppressMessage("Interoperability", "CA1416:Validate platform compatibility")]
internal class WindowsAutoStartService : IAutoStartService
{
    private const string AppName = "BorgMate";
    private const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
        if (key is null) return;

        if (enabled)
        {
            var exePath = Environment.ProcessPath ?? "BorgMate.exe";
            key.SetValue(AppName, $"\"{exePath}\"");
        }
        else
        {
            key.DeleteValue(AppName, false);
        }
    }

    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(AppName) is not null;
    }
}