using System;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using BorgMate.ViewModels;

namespace BorgMate.Views;

public abstract class ModalWindow : Window
{
    private ISaveable? _trackedSaveable;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_trackedSaveable is not null)
        {
            _trackedSaveable.PropertyChanged -= OnSaveablePropertyChanged;
            _trackedSaveable = null;
        }

        if (DataContext is ISaveable saveable)
        {
            _trackedSaveable = saveable;
            saveable.PropertyChanged += OnSaveablePropertyChanged;
        }
    }

    private void OnSaveablePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(ISaveable.IsSaved) && _trackedSaveable?.IsSaved == true)
            Close();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        // macOS: Cancel then Submit (right-most is primary) — this is the AXAML order
        // Windows/Linux: Submit then Cancel (left-most is primary) — reverse children
        if (!OperatingSystem.IsMacOS())
            ReverseDialogButtons();

        HideMinimizeMaximizeButtons();

        if (OperatingSystem.IsMacOS())
            AttachAsChildWindowMacOs();
    }

    protected override void OnClosed(EventArgs e)
    {
        // Unsubscribe and clear DataContext so bindings detach and the old VM/window
        // can be collected — stale binding references from closed windows cause
        // Avalonia's compiled binding system to freeze on subsequent window creation.
        if (_trackedSaveable is not null)
        {
            _trackedSaveable.PropertyChanged -= OnSaveablePropertyChanged;
            _trackedSaveable = null;
        }
        DataContext = null;

        if (OperatingSystem.IsMacOS())
            DetachChildWindowMacOs();
        base.OnClosed(e);
    }

    private void HideMinimizeMaximizeButtons()
    {
        if (OperatingSystem.IsMacOS())
            HideButtonsMacOs();
        else if (OperatingSystem.IsWindows())
            HideButtonsWindows();
        // Linux: CanResize="False" + ShowDialog already disables maximize/minimize
        // on most window managers (GNOME, KDE, XFCE). No additional action needed.
    }

    private void HideButtonsMacOs()
    {
        try
        {
            var handle = TryGetPlatformHandle();
            if (handle?.Handle is null or 0) return;
            var nsWindow = handle.Handle;

            var sel_standardWindowButton = sel_registerName("standardWindowButton:");
            var sel_setHidden = sel_registerName("setHidden:");

            // NSWindowMiniaturizeButton = 1, NSWindowZoomButton = 2
            for (ulong i = 1; i <= 2; i++)
            {
                var button = objc_msgSend_retPtr(nsWindow, sel_standardWindowButton, i);
                if (button != IntPtr.Zero)
                    objc_msgSend_void(button, sel_setHidden, true);
            }
        }
        catch { /* best-effort */ }
    }

    private void HideButtonsWindows()
    {
        try
        {
            var handle = TryGetPlatformHandle();
            if (handle?.Handle is null or 0) return;
            var hwnd = handle.Handle;

            const int GWL_STYLE = -16;
            const int WS_MINIMIZEBOX = 0x00020000;
            const int WS_MAXIMIZEBOX = 0x00010000;

            var style = GetWindowLong(hwnd, GWL_STYLE);
            SetWindowLong(hwnd, GWL_STYLE, style & ~WS_MINIMIZEBOX & ~WS_MAXIMIZEBOX);
        }
        catch { /* best-effort */ }
    }

    private IntPtr _parentNsWindow;
    private IntPtr _childNsWindow;

    private void AttachAsChildWindowMacOs()
    {
        try
        {
            var parentHandle = (Owner as Window)?.TryGetPlatformHandle();
            var childHandle = TryGetPlatformHandle();
            if (parentHandle?.Handle is null or 0 || childHandle?.Handle is null or 0) return;

            _parentNsWindow = parentHandle.Handle;
            _childNsWindow = childHandle.Handle;

            // [parentNSWindow addChildWindow:dialogNSWindow ordered:NSWindowAbove]
            // NSWindowAbove = 1
            var sel = sel_registerName("addChildWindow:ordered:");
            objc_msgSend_addChild(_parentNsWindow, sel, _childNsWindow, 1);
        }
        catch { /* best-effort */ }
    }

    private void DetachChildWindowMacOs()
    {
        try
        {
            if (_parentNsWindow == IntPtr.Zero || _childNsWindow == IntPtr.Zero) return;

            // [parentNSWindow removeChildWindow:dialogNSWindow]
            var sel = sel_registerName("removeChildWindow:");
            objc_msgSend_ptr(_parentNsWindow, sel, _childNsWindow);

            _parentNsWindow = IntPtr.Zero;
            _childNsWindow = IntPtr.Zero;
        }
        catch { /* best-effort */ }
    }

    // macOS ObjC interop
    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "sel_registerName")]
    private static extern IntPtr sel_registerName(string name);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend_retPtr(IntPtr receiver, IntPtr selector, ulong arg);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSend_void(IntPtr receiver, IntPtr selector, bool arg);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSend_addChild(IntPtr receiver, IntPtr selector, IntPtr window, long ordered);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSend_ptr(IntPtr receiver, IntPtr selector, IntPtr arg);

    // Windows interop
    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    private void ReverseDialogButtons()
    {
        var panel = this.GetVisualDescendants()
            .OfType<StackPanel>()
            .FirstOrDefault(p => p.Classes.Contains("dialog-buttons"));

        if (panel is null) return;

        var children = panel.Children.ToList();
        panel.Children.Clear();
        children.Reverse();
        foreach (var child in children)
            panel.Children.Add(child);
    }
}
