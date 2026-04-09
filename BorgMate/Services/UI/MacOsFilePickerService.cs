using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace BorgMate.Services.UI;

/// <summary>
/// Native macOS file picker using NSOpenPanel via ObjC interop.
/// Bypasses Avalonia's StorageProvider which has a use-after-free crash
/// in the async completion callback (the managed IAvnSystemDialogEvents
/// wrapper can be GC'd before the XPC completion fires).
/// Uses synchronous runModal which runs a nested run loop, avoiding
/// the callback lifetime issue entirely.
/// </summary>
public class MacOsFilePickerService : IFilePickerService
{
    private const nint NSModalResponseOK = 1;

    public Task<string?> PickFolderAsync(string title = "Select Folder")
    {
        var panel = CreatePanel(canChooseFiles: false, canChooseDirectories: true,
            allowsMultiple: false, message: title);
        var result = RunModal(panel) == NSModalResponseOK ? GetUrl(panel) : null;
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<string>> PickFoldersAsync(string title = "Select Folders")
    {
        var panel = CreatePanel(canChooseFiles: false, canChooseDirectories: true,
            allowsMultiple: true, message: title);
        IReadOnlyList<string> result = RunModal(panel) == NSModalResponseOK ? GetUrls(panel) : [];
        return Task.FromResult(result);
    }

    public Task<string?> PickFileAsync(string title = "Select File")
    {
        var panel = CreatePanel(canChooseFiles: true, canChooseDirectories: false,
            allowsMultiple: false, message: title);
        var result = RunModal(panel) == NSModalResponseOK ? GetUrl(panel) : null;
        return Task.FromResult(result);
    }

    private static IntPtr CreatePanel(bool canChooseFiles, bool canChooseDirectories,
        bool allowsMultiple, string message)
    {
        var panel = objc_msgSend_ret(objc_getClass("NSOpenPanel"), sel_registerName("openPanel"));
        objc_msgSend_bool(panel, sel_registerName("setCanChooseFiles:"), canChooseFiles);
        objc_msgSend_bool(panel, sel_registerName("setCanChooseDirectories:"), canChooseDirectories);
        objc_msgSend_bool(panel, sel_registerName("setAllowsMultipleSelection:"), allowsMultiple);

        var nsMessage = CreateNSString(message);
        objc_msgSend_ptr(panel, sel_registerName("setMessage:"), nsMessage);
        return panel;
    }

    private static nint RunModal(IntPtr panel) =>
        objc_msgSend_nint(panel, sel_registerName("runModal"));

    private static string? GetUrl(IntPtr panel)
    {
        var url = objc_msgSend_ret(panel, sel_registerName("URL"));
        return url == IntPtr.Zero ? null : NsUrlToPath(url);
    }

    private static IReadOnlyList<string> GetUrls(IntPtr panel)
    {
        var urls = objc_msgSend_ret(panel, sel_registerName("URLs"));
        if (urls == IntPtr.Zero) return [];

        var count = (int)objc_msgSend_nint(urls, sel_registerName("count"));
        var selObjectAtIndex = sel_registerName("objectAtIndex:");
        var paths = new List<string>(count);

        for (var i = 0; i < count; i++)
        {
            var url = objc_msgSend_nint_ret(urls, selObjectAtIndex, (nint)i);
            if (url == IntPtr.Zero) continue;
            var path = NsUrlToPath(url);
            if (path is not null) paths.Add(path);
        }

        return paths;
    }

    private static string? NsUrlToPath(IntPtr nsUrl)
    {
        var nsPath = objc_msgSend_ret(nsUrl, sel_registerName("path"));
        if (nsPath == IntPtr.Zero) return null;
        var utf8 = objc_msgSend_ret(nsPath, sel_registerName("UTF8String"));
        return Marshal.PtrToStringUTF8(utf8);
    }

    private static IntPtr CreateNSString(string str)
    {
        var utf8 = Marshal.StringToCoTaskMemUTF8(str);
        try
        {
            return objc_msgSend_ptr_ret(
                objc_getClass("NSString"),
                sel_registerName("stringWithUTF8String:"),
                utf8);
        }
        finally
        {
            Marshal.FreeCoTaskMem(utf8);
        }
    }

    [DllImport("/usr/lib/libobjc.dylib")]
    private static extern IntPtr objc_getClass(string name);

    [DllImport("/usr/lib/libobjc.dylib")]
    private static extern IntPtr sel_registerName(string name);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend_ret(IntPtr receiver, IntPtr selector);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSend_bool(IntPtr receiver, IntPtr selector, [MarshalAs(UnmanagedType.I1)] bool value);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSend_ptr(IntPtr receiver, IntPtr selector, IntPtr value);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend_ptr_ret(IntPtr receiver, IntPtr selector, IntPtr arg);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern nint objc_msgSend_nint(IntPtr receiver, IntPtr selector);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend_nint_ret(IntPtr receiver, IntPtr selector, nint arg);
}
