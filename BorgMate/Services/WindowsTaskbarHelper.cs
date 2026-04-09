using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace BorgMate.Services;

/// <summary>
/// Controls the Windows taskbar progress indicator overlay.
/// Uses ITaskbarList3 COM interface.
/// </summary>
[SupportedOSPlatform("windows")]
public static class WindowsTaskbarHelper
{
    private static ITaskbarList3? _taskbar;
    private static bool _initFailed;

    public static void SetIndeterminate(IntPtr hwnd)
    {
        if (!EnsureTaskbar() || hwnd == IntPtr.Zero) return;
        try { _taskbar!.SetProgressState(hwnd, TBPF.Indeterminate); } catch { /* best-effort */ }
    }

    public static void Clear(IntPtr hwnd)
    {
        if (!EnsureTaskbar() || hwnd == IntPtr.Zero) return;
        try { _taskbar!.SetProgressState(hwnd, TBPF.NoProgress); } catch { /* best-effort */ }
    }

    private static bool EnsureTaskbar()
    {
        if (_taskbar is not null) return true;
        if (_initFailed) return false;

        try
        {
            _taskbar = (ITaskbarList3)new TaskbarInstance();
            _taskbar.HrInit();
            return true;
        }
        catch
        {
            _initFailed = true;
            return false;
        }
    }

    private enum TBPF
    {
        NoProgress = 0,
        Indeterminate = 0x1,
    }

    [ComImport]
    [Guid("ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITaskbarList3
    {
        // ITaskbarList
        void HrInit();
        void AddTab(IntPtr hwnd);
        void DeleteTab(IntPtr hwnd);
        void ActivateTab(IntPtr hwnd);
        void SetActiveAlt(IntPtr hwnd);

        // ITaskbarList2
        void MarkFullscreenWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool fFullscreen);

        // ITaskbarList3
        void SetProgressValue(IntPtr hwnd, ulong ullCompleted, ulong ullTotal);
        void SetProgressState(IntPtr hwnd, TBPF tbpfFlags);
    }

    [ComImport]
    [Guid("56fdf344-fd6d-11d0-958a-006097c9a090")]
    [ClassInterface(ClassInterfaceType.None)]
    private class TaskbarInstance { }
}
