using System;
using System.Runtime.InteropServices;
using System.Text;

namespace BorgMate.Infrastructure.Platform;

/// <summary>Minimal CoreFoundation P/Invoke surface shared by the macOS framework integrations.</summary>
internal static class CoreFoundationNative
{
    private const string CoreFoundationLib =
        "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    private const uint StringEncodingUtf8 = 0x08000100;

    public static IntPtr BooleanTrue => ReadConstant(NativeLibrary.Load(CoreFoundationLib), "kCFBooleanTrue");

    /// <summary>Reads an exported global constant such as a <c>CFStringRef</c> key from <paramref name="library" />.</summary>
    public static IntPtr ReadConstant(IntPtr library, string name)
    {
        return Marshal.ReadIntPtr(NativeLibrary.GetExport(library, name));
    }

    public static IntPtr CreateString(string value)
    {
        return CFStringCreateWithCString(IntPtr.Zero, value, StringEncodingUtf8);
    }

    public static IntPtr CreateMutableDictionary()
    {
        var library = NativeLibrary.Load(CoreFoundationLib);
        return CFDictionaryCreateMutable(IntPtr.Zero, 0,
            NativeLibrary.GetExport(library, "kCFTypeDictionaryKeyCallBacks"),
            NativeLibrary.GetExport(library, "kCFTypeDictionaryValueCallBacks"));
    }

    public static IntPtr CreateData(byte[] bytes)
    {
        return CFDataCreate(IntPtr.Zero, bytes, bytes.Length);
    }

    public static byte[] GetBytes(IntPtr data)
    {
        var bytes = new byte[CFDataGetLength(data)];
        Marshal.Copy(CFDataGetBytePtr(data), bytes, 0, bytes.Length);
        return bytes;
    }

    /// <summary>Returns null when the UTF-8 form of <paramref name="value" /> does not fit <paramref name="maxBytes" />.</summary>
    public static string? GetString(IntPtr value, int maxBytes)
    {
        var buffer = new byte[maxBytes];
        if (!CFStringGetCString(value, buffer, buffer.Length, StringEncodingUtf8))
            return null;

        var length = Array.IndexOf(buffer, (byte)0);
        return Encoding.UTF8.GetString(buffer, 0, length < 0 ? buffer.Length : length);
    }

    [DllImport(CoreFoundationLib)]
    public static extern IntPtr CFURLCreateWithString(IntPtr allocator, IntPtr urlString, IntPtr baseUrl);

    [DllImport(CoreFoundationLib)]
    public static extern void CFDictionarySetValue(IntPtr dictionary, IntPtr key, IntPtr value);

    [DllImport(CoreFoundationLib)]
    public static extern nint CFArrayGetCount(IntPtr array);

    [DllImport(CoreFoundationLib)]
    public static extern IntPtr CFArrayGetValueAtIndex(IntPtr array, nint index);

    [DllImport(CoreFoundationLib)]
    public static extern void CFRelease(IntPtr value);

    [DllImport(CoreFoundationLib)]
    private static extern IntPtr CFStringCreateWithCString(IntPtr allocator,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string value, uint encoding);

    [DllImport(CoreFoundationLib)]
    private static extern IntPtr CFDictionaryCreateMutable(IntPtr allocator, nint capacity,
        IntPtr keyCallbacks, IntPtr valueCallbacks);

    [DllImport(CoreFoundationLib)]
    private static extern IntPtr CFDataCreate(IntPtr allocator, byte[] bytes, nint length);

    [DllImport(CoreFoundationLib)]
    private static extern nint CFDataGetLength(IntPtr data);

    [DllImport(CoreFoundationLib)]
    private static extern IntPtr CFDataGetBytePtr(IntPtr data);

    [DllImport(CoreFoundationLib)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool CFStringGetCString(IntPtr value, byte[] buffer, nint bufferSize, uint encoding);
}
