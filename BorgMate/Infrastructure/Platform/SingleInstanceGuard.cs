using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BorgMate.Infrastructure.Platform;

/// <summary>
///     Single-instance guard built on a per-user named mutex and a named pipe that only the same user
///     can open. The first instance owns the mutex and listens on the pipe; a later launch signals it
///     and exits.
/// </summary>
public class SingleInstanceGuard : IDisposable
{
    private const string ActivateMessage = "ACTIVATE";
    private const string WakeMessage = "WAKE";
    private const int MaxMessageBytes = 16;
    private static readonly TimeSpan SignalTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ListenerRetryDelay = TimeSpan.FromSeconds(1);

    private static readonly string InstanceName = "BorgMate-" + Environment.UserName.Replace('\\', '_')
        .Replace('/', '_');

    private CancellationTokenSource? _cts;
    private Mutex? _mutex;
    private bool _ownsMutex;

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        if (_ownsMutex)
            _mutex?.ReleaseMutex();
        _mutex?.Dispose();
    }

    public event Action? ActivationRequested;

    /// <summary>Raised when a --scheduled-wake launch defers to this running instance.</summary>
    public event Action? WakeRequested;

    /// <summary>
    ///     Returns true if this is the first instance (caller should proceed).
    ///     Returns false if another instance is running (caller should exit after it was signaled).
    /// </summary>
    public bool TryAcquire(bool activateExisting = true)
    {
        _mutex = new Mutex(false, InstanceName,
            new NamedWaitHandleOptions { CurrentUserOnly = true, CurrentSessionOnly = true });
        try
        {
            _ownsMutex = _mutex.WaitOne(TimeSpan.Zero);
        }
        catch (AbandonedMutexException)
        {
            // The previous owner crashed; the mutex now belongs to this instance.
            _ownsMutex = true;
        }

        if (!_ownsMutex)
        {
            SignalExisting(activateExisting ? ActivateMessage : WakeMessage);
            return false;
        }

        _cts = new CancellationTokenSource();
        _ = Task.Run(() => ListenLoop(_cts.Token));
        return true;
    }

    private static void SignalExisting(string message)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", InstanceName, PipeDirection.Out,
                PipeOptions.CurrentUserOnly);
            client.Connect(SignalTimeout);
            client.Write(Encoding.ASCII.GetBytes(message + "\n"));
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
        {
            // The running instance owns the mutex but is not listening yet (still starting) or is
            // exiting; it shows its window or checks schedules on its own, so this launch only exits.
            Console.Error.WriteLine($"SingleInstanceGuard could not signal the running instance: {ex.Message}");
        }
    }

    private async Task ListenLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
            try
            {
                await using var server = new NamedPipeServerStream(InstanceName, PipeDirection.In, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(ct);
                var message = await ReadMessageAsync(server, ct);
                if (message == ActivateMessage)
                    ActivationRequested?.Invoke();
                else if (message == WakeMessage)
                    WakeRequested?.Invoke();
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"SingleInstanceGuard listener error: {ex.Message}");
                // A pipe that cannot be created fails again at once; pausing keeps the loop from spinning.
                await Task.Delay(ListenerRetryDelay, ct).ConfigureAwait(false);
            }
    }

    /// <summary>
    ///     Reads one newline-terminated message. A client that sends more than a message's length or
    ///     stays silent is dropped, so it cannot hold the single listener or grow memory.
    /// </summary>
    internal static async Task<string?> ReadMessageAsync(Stream stream, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(SignalTimeout);
        var buffer = new byte[MaxMessageBytes + 1];
        var length = 0;
        try
        {
            while (length < buffer.Length)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(length), timeout.Token);
                if (read == 0)
                    break;
                var newline = Array.IndexOf(buffer, (byte)'\n', length, read);
                if (newline >= 0)
                    return Encoding.ASCII.GetString(buffer, 0, newline).TrimEnd('\r');
                length += read;
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return null;
        }

        return null;
    }
}
