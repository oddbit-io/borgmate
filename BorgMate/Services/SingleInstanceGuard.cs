using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace BorgMate.Services;

/// <summary>
/// Cross-platform single-instance guard using a lock file + TCP loopback.
/// First instance writes a TCP port to a lock file and listens for activation signals.
/// Second instance reads the port, sends "ACTIVATE", and exits.
/// </summary>
public class SingleInstanceGuard : IDisposable
{
    private static readonly string LockFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "BorgMate", ".lock");

    private TcpListener? _listener;
    private CancellationTokenSource? _cts;

    public event Action? ActivationRequested;

    /// <summary>
    /// Returns true if this is the first instance (caller should proceed).
    /// Returns false if another instance is running (caller should exit).
    /// </summary>
    public bool TryAcquire()
    {
        // Check if another instance is running
        if (File.Exists(LockFilePath))
        {
            if (int.TryParse(File.ReadAllText(LockFilePath).Trim(), out var port) && port > 0)
            {
                if (TrySignalExisting(port))
                    return false; // Other instance responded, exit
            }
            // Stale lock file — other instance crashed
        }

        // We are the first instance
        StartListener();
        return true;
    }

    private static bool TrySignalExisting(int port)
    {
        try
        {
            using var client = new TcpClient();
            client.Connect(IPAddress.Loopback, port);
            using var writer = new StreamWriter(client.GetStream()) { AutoFlush = true };
            writer.WriteLine("ACTIVATE");
            return true;
        }
        catch
        {
            return false; // Can't connect — stale lock
        }
    }

    private void StartListener()
    {
        _cts = new CancellationTokenSource();
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        var port = ((IPEndPoint)_listener.LocalEndpoint).Port;

        Directory.CreateDirectory(Path.GetDirectoryName(LockFilePath)!);
        File.WriteAllText(LockFilePath, port.ToString());

        _ = Task.Run(() => ListenLoop(_cts.Token));
    }

    private async Task ListenLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var client = await _listener!.AcceptTcpClientAsync(ct);
                using (client)
                {
                    using var reader = new StreamReader(client.GetStream());
                    var line = await reader.ReadLineAsync(ct);
                    if (line == "ACTIVATE")
                        ActivationRequested?.Invoke();
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { Console.Error.WriteLine($"SingleInstanceGuard listener error: {ex.Message}"); }
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _listener?.Stop();
        _listener?.Dispose();
        _cts?.Dispose();
        try { File.Delete(LockFilePath); } catch { /* best effort */ }
    }
}
