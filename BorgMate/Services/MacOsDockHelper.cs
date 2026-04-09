using System;
using System.IO;
using static BorgMate.Services.ObjCRuntime;

namespace BorgMate.Services;

public static class MacOsDockHelper
{
    private const long PolicyRegular = 0;
    private const long PolicyAccessory = 1;

    private static IntPtr _iconImage = IntPtr.Zero;

    public static void ShowInDock()
    {
        if (!OperatingSystem.IsMacOS()) return;
        SetActivationPolicy(PolicyRegular);
        SetDockIcon();
    }

    public static void HideFromDock()
    {
        if (!OperatingSystem.IsMacOS()) return;
        SetActivationPolicy(PolicyAccessory);
    }

    /// <summary>
    /// Load icon from embedded resource and cache the NSImage pointer.
    /// </summary>
    public static void LoadIcon(Stream iconStream)
    {
        if (!OperatingSystem.IsMacOS()) return;

        using var ms = new MemoryStream();
        iconStream.CopyTo(ms);
        var bytes = ms.ToArray();

        var nsDataClass = objc_getClass("NSData");
        var dataPtr = objc_msgSend(nsDataClass, sel_registerName("dataWithBytes:length:"),
            bytes, (ulong)bytes.Length);

        var nsImageClass = objc_getClass("NSImage");
        var imageAlloc = objc_msgSend(nsImageClass, sel_registerName("alloc"));
        _iconImage = objc_msgSend(imageAlloc, sel_registerName("initWithData:"), dataPtr);
    }

    private static void SetDockIcon()
    {
        if (_iconImage == IntPtr.Zero) return;

        var nsApp = objc_getClass("NSApplication");
        var sharedApp = objc_msgSend(nsApp, sel_registerName("sharedApplication"));
        objc_msgSend_void(sharedApp, sel_registerName("setApplicationIconImage:"), _iconImage);
    }

    private static IntPtr _progressIndicator = IntPtr.Zero;
    private static IntPtr _containerView = IntPtr.Zero;
    private static bool _isDeterminate = true;

    /// <summary>
    /// Sets the dock tile progress bar. Pass 0–100 for determinate, -1 for indeterminate, null to hide.
    /// </summary>
    public static void SetProgress(double? progress)
    {
        if (!OperatingSystem.IsMacOS()) return;

        var nsApp = objc_getClass("NSApplication");
        var sharedApp = objc_msgSend(nsApp, sel_registerName("sharedApplication"));
        var dockTile = objc_msgSend(sharedApp, sel_registerName("dockTile"));

        if (progress is null)
        {
            if (_progressIndicator != IntPtr.Zero)
            {
                objc_msgSend_void(dockTile, sel_registerName("setContentView:"), IntPtr.Zero);
                _progressIndicator = IntPtr.Zero;
                _containerView = IntPtr.Zero;
                _isDeterminate = true;
                SetDockIcon();
                objc_msgSend_void(dockTile, sel_registerName("display"));
            }
            return;
        }

        if (_progressIndicator == IntPtr.Zero)
        {
            var size = objc_msgSend_stret(dockTile, sel_registerName("size"));
            var barHeight = 14.0;

            // Container view fills the dock tile
            var nsViewClass = objc_getClass("NSView");
            var viewAlloc = objc_msgSend(nsViewClass, sel_registerName("alloc"));
            _containerView = objc_msgSend(viewAlloc, sel_registerName("initWithFrame:"),
                0.0, 0.0, size.Width, size.Height);

            // App icon as background so it remains visible
            if (_iconImage != IntPtr.Zero)
            {
                var ivClass = objc_getClass("NSImageView");
                var ivAlloc = objc_msgSend(ivClass, sel_registerName("alloc"));
                var imageView = objc_msgSend(ivAlloc, sel_registerName("initWithFrame:"),
                    0.0, 0.0, size.Width, size.Height);
                objc_msgSend_void(imageView, sel_registerName("setImage:"), _iconImage);
                objc_msgSend_void(_containerView, sel_registerName("addSubview:"), imageView);
            }

            // Progress bar at the bottom
            var piClass = objc_getClass("NSProgressIndicator");
            var piAlloc = objc_msgSend(piClass, sel_registerName("alloc"));
            _progressIndicator = objc_msgSend(piAlloc, sel_registerName("initWithFrame:"),
                0.0, 0.0, size.Width, barHeight);

            objc_msgSend_void(_progressIndicator, sel_registerName("setMinValue:"), 0.0);
            objc_msgSend_void(_progressIndicator, sel_registerName("setMaxValue:"), 100.0);

            objc_msgSend_void(_containerView, sel_registerName("addSubview:"), _progressIndicator);
            objc_msgSend_void(dockTile, sel_registerName("setContentView:"), _containerView);
        }

        var isDeterminate = progress >= 0;
        if (isDeterminate != _isDeterminate)
        {
            _isDeterminate = isDeterminate;
            objc_msgSend_void(_progressIndicator, sel_registerName("setIndeterminate:"), !isDeterminate);
            if (!isDeterminate)
                objc_msgSend_void(_progressIndicator, sel_registerName("startAnimation:"));
        }

        if (isDeterminate)
            objc_msgSend_void(_progressIndicator, sel_registerName("setDoubleValue:"), progress.Value);

        objc_msgSend_void(dockTile, sel_registerName("display"));
    }

    private static void SetActivationPolicy(long policy)
    {
        var nsApp = objc_getClass("NSApplication");
        var sharedApp = objc_msgSend(nsApp, sel_registerName("sharedApplication"));
        objc_msgSend_void(sharedApp, sel_registerName("setActivationPolicy:"), policy);
    }
}
