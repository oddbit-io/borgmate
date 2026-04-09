using System;
using System.Runtime.InteropServices;
using System.Text;
using static BorgMate.Services.ObjCRuntime;

namespace BorgMate.Services.Notifications;

public class MacOsNotificationService : INotificationService
{
    public void Send(string title, string body)
    {
        var script = $"display notification \"{StringHelpers.EscapeShell(body)}\" with title \"{StringHelpers.AppName}\" subtitle \"{StringHelpers.EscapeShell(title)}\"";
        var nsScript = ToNS(script);

        var appleScript = objc_msgSend(
            objc_msgSend(objc_getClass("NSAppleScript"), sel_registerName("alloc")),
            sel_registerName("initWithSource:"), nsScript);

        // executeAndReturnError: takes a pointer to an NSDictionary* (out param)
        var errorPtr = Marshal.AllocHGlobal(IntPtr.Size);
        Marshal.WriteIntPtr(errorPtr, IntPtr.Zero);
        objc_msgSend(appleScript, sel_registerName("executeAndReturnError:"), errorPtr);
        Marshal.FreeHGlobal(errorPtr);

        // Release ObjC objects to prevent memory leaks
        var release = sel_registerName("release");
        objc_msgSend_void(appleScript, release);
        objc_msgSend_void(nsScript, release);
    }

    private static nint ToNS(string str)
    {
        var bytes = Encoding.UTF8.GetBytes(str + '\0');
        var ptr = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, ptr, bytes.Length);
        var ns = objc_msgSend(
            objc_msgSend(objc_getClass("NSString"), sel_registerName("alloc")),
            sel_registerName("initWithUTF8String:"), ptr);
        Marshal.FreeHGlobal(ptr);
        return ns;
    }
}
