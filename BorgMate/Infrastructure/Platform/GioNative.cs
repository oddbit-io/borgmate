using System;
using System.Runtime.InteropServices;

namespace BorgMate.Infrastructure.Platform;

/// <summary>
///     Minimal GLib/GObject/GIO P/Invoke surface for mounting GVFS locations.
///     Talks to the same gvfs daemons as `gio mount`, but reports failures as typed
///     GError codes instead of localized CLI text.
/// </summary>
internal static class GioNative
{
    private const string GLibLib = "libglib-2.0.so.0";
    private const string GObjectLib = "libgobject-2.0.so.0";
    private const string GioLib = "libgio-2.0.so.0";

    // GMountOperationResult
    public const int MountOperationHandled = 0;
    public const int MountOperationAborted = 1;

    // GIOErrorEnum values (gio/gioenums.h) used for classification.
    public const int IoErrorPermissionDenied = 14;
    public const int IoErrorNotSupported = 15;
    public const int IoErrorAlreadyMounted = 17;
    public const int IoErrorTimedOut = 24;
    public const int IoErrorHostNotFound = 28;
    public const int IoErrorFailedHandled = 30;
    public const int IoErrorDbusError = 36;
    public const int IoErrorHostUnreachable = 37;
    public const int IoErrorNetworkUnreachable = 38;
    public const int IoErrorConnectionRefused = 39;

    /// <summary>GMountOperation "ask-password" signal handler.</summary>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void AskPasswordHandler(
        IntPtr operation, IntPtr message, IntPtr defaultUser, IntPtr defaultDomain, int flags, IntPtr userData);

    /// <summary>GAsyncReadyCallback completion handler.</summary>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void AsyncReadyCallback(IntPtr sourceObject, IntPtr result, IntPtr userData);

    [StructLayout(LayoutKind.Sequential)]
    public struct GError
    {
        public uint Domain;
        public int Code;
        public IntPtr Message;
    }

    [DllImport(GLibLib)]
    public static extern IntPtr g_main_context_new();

    [DllImport(GLibLib)]
    public static extern void g_main_context_push_thread_default(IntPtr context);

    [DllImport(GLibLib)]
    public static extern void g_main_context_pop_thread_default(IntPtr context);

    [DllImport(GLibLib)]
    public static extern void g_main_context_unref(IntPtr context);

    [DllImport(GLibLib)]
    public static extern IntPtr g_main_loop_new(IntPtr context, bool isRunning);

    [DllImport(GLibLib)]
    public static extern void g_main_loop_run(IntPtr loop);

    [DllImport(GLibLib)]
    public static extern void g_main_loop_quit(IntPtr loop);

    [DllImport(GLibLib)]
    public static extern void g_main_loop_unref(IntPtr loop);

    [DllImport(GLibLib)]
    public static extern void g_error_free(IntPtr error);

    [DllImport(GLibLib)]
    public static extern IntPtr g_get_user_runtime_dir();

    [DllImport(GObjectLib)]
    public static extern void g_object_unref(IntPtr instance);

    [DllImport(GObjectLib)]
    public static extern nuint g_signal_connect_data(IntPtr instance,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string detailedSignal,
        IntPtr handler, IntPtr data, IntPtr destroyData, int connectFlags);

    [DllImport(GioLib)]
    public static extern IntPtr g_file_new_for_uri([MarshalAs(UnmanagedType.LPUTF8Str)] string uri);

    [DllImport(GioLib)]
    public static extern void g_file_mount_enclosing_volume(IntPtr file, int flags,
        IntPtr mountOperation, IntPtr cancellable, IntPtr callback, IntPtr userData);

    [DllImport(GioLib)]
    public static extern bool g_file_mount_enclosing_volume_finish(IntPtr file, IntPtr result, out IntPtr error);

    [DllImport(GioLib)]
    public static extern IntPtr g_mount_operation_new();

    [DllImport(GioLib)]
    public static extern void g_mount_operation_set_username(IntPtr operation,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string username);

    [DllImport(GioLib)]
    public static extern void g_mount_operation_set_password(IntPtr operation,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string password);

    [DllImport(GioLib)]
    public static extern void g_mount_operation_set_anonymous(IntPtr operation, bool anonymous);

    [DllImport(GioLib)]
    public static extern void g_mount_operation_reply(IntPtr operation, int result);

    [DllImport(GioLib)]
    public static extern uint g_io_error_quark();
}
