using System;
using System.Runtime.InteropServices;
using System.Text;

namespace BorgMate.Services.Notifications;

public class MacOsNotificationService : INotificationService
{
    public void Send(string title, string body)
    {
        var script = $"display notification \"{Escape(body)}\" with title \"BorgMate\" subtitle \"{Escape(title)}\"";
        var nsScript = ToNS(script);

        var appleScript = objc_msgSend_ptr_ret(
            objc_msgSend_ret(objc_getClass("NSAppleScript"), sel_registerName("alloc")),
            sel_registerName("initWithSource:"), nsScript);

        // executeAndReturnError: takes a pointer to an NSDictionary* (out param)
        var errorPtr = Marshal.AllocHGlobal(IntPtr.Size);
        Marshal.WriteIntPtr(errorPtr, IntPtr.Zero);
        objc_msgSend_ptr_ret(appleScript, sel_registerName("executeAndReturnError:"), errorPtr);
        Marshal.FreeHGlobal(errorPtr);

        // Release ObjC objects to prevent memory leaks
        var release = sel_registerName("release");
        objc_msgSend_void(appleScript, release);
        objc_msgSend_void(nsScript, release);
    }

    private static string Escape(string s) =>
        s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ");

    private static nint ToNS(string str)
    {
        var bytes = Encoding.UTF8.GetBytes(str + '\0');
        var ptr = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, ptr, bytes.Length);
        var ns = objc_msgSend_ptr_ret(
            objc_msgSend_ret(objc_getClass("NSString"), sel_registerName("alloc")),
            sel_registerName("initWithUTF8String:"), ptr);
        Marshal.FreeHGlobal(ptr);
        return ns;
    }

    [DllImport("libobjc.dylib", CharSet = CharSet.Ansi)]
    private static extern nint objc_getClass(string name);

    [DllImport("libobjc.dylib", CharSet = CharSet.Ansi)]
    private static extern nint sel_registerName(string name);

    [DllImport("libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern nint objc_msgSend_ret(nint target, nint sel);

    [DllImport("libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern nint objc_msgSend_ptr_ret(nint target, nint sel, nint a1);

    [DllImport("libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSend_void(nint target, nint sel);
}
