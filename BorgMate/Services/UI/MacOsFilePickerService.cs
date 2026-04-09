using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using static BorgMate.Services.ObjCRuntime;

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

    public Task<string?> PickFolderAsync(string title)
    {
        var panel = CreatePanel(canChooseFiles: false, canChooseDirectories: true,
            allowsMultiple: false, message: title);
        var result = RunModal(panel) == NSModalResponseOK ? GetUrl(panel) : null;
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<string>> PickFoldersAsync(string title)
    {
        var panel = CreatePanel(canChooseFiles: false, canChooseDirectories: true,
            allowsMultiple: true, message: title);
        IReadOnlyList<string> result = RunModal(panel) == NSModalResponseOK ? GetUrls(panel) : [];
        return Task.FromResult(result);
    }

    public Task<string?> PickFileAsync(string title)
    {
        var panel = CreatePanel(canChooseFiles: true, canChooseDirectories: false,
            allowsMultiple: false, message: title);
        var result = RunModal(panel) == NSModalResponseOK ? GetUrl(panel) : null;
        return Task.FromResult(result);
    }

    private static IntPtr CreatePanel(bool canChooseFiles, bool canChooseDirectories,
        bool allowsMultiple, string message)
    {
        var panel = objc_msgSend(objc_getClass("NSOpenPanel"), sel_registerName("openPanel"));
        objc_msgSend_void(panel, sel_registerName("setCanChooseFiles:"), canChooseFiles);
        objc_msgSend_void(panel, sel_registerName("setCanChooseDirectories:"), canChooseDirectories);
        objc_msgSend_void(panel, sel_registerName("setAllowsMultipleSelection:"), allowsMultiple);

        var nsMessage = CreateNSString(message);
        objc_msgSend_void(panel, sel_registerName("setMessage:"), nsMessage);
        return panel;
    }

    private static nint RunModal(IntPtr panel) =>
        (nint)objc_msgSend(panel, sel_registerName("runModal"));

    private static string? GetUrl(IntPtr panel)
    {
        var url = objc_msgSend(panel, sel_registerName("URL"));
        return url == IntPtr.Zero ? null : NsUrlToPath(url);
    }

    private static IReadOnlyList<string> GetUrls(IntPtr panel)
    {
        var urls = objc_msgSend(panel, sel_registerName("URLs"));
        if (urls == IntPtr.Zero) return [];

        var count = (nint)objc_msgSend(urls, sel_registerName("count"));
        var selObjectAtIndex = sel_registerName("objectAtIndex:");
        var paths = new List<string>((int)count);

        for (nint i = 0; i < count; i++)
        {
            var url = objc_msgSend(urls, selObjectAtIndex, i);
            if (url == IntPtr.Zero) continue;
            var path = NsUrlToPath(url);
            if (path is not null) paths.Add(path);
        }

        return paths;
    }

    private static string? NsUrlToPath(IntPtr nsUrl)
    {
        var nsPath = objc_msgSend(nsUrl, sel_registerName("path"));
        if (nsPath == IntPtr.Zero) return null;
        var utf8 = objc_msgSend(nsPath, sel_registerName("UTF8String"));
        return Marshal.PtrToStringUTF8(utf8);
    }

    private static IntPtr CreateNSString(string str)
    {
        var utf8 = Marshal.StringToCoTaskMemUTF8(str);
        try
        {
            return objc_msgSend(
                objc_getClass("NSString"),
                sel_registerName("stringWithUTF8String:"),
                utf8);
        }
        finally
        {
            Marshal.FreeCoTaskMem(utf8);
        }
    }
}
