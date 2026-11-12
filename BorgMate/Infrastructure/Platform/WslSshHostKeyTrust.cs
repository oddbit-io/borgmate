using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Threading;
using BorgMate.Infrastructure.Borg;
using BorgMate.Presentation.Dialogs;
using BorgMate.Runtime.Orchestration;
using BorgMate.Runtime.Security;
using Microsoft.Extensions.Logging;

namespace BorgMate.Infrastructure.Platform;

/// <summary>
///     Adds SSH host keys to WSL's known_hosts only after the user confirms their fingerprints.
///     ssh under WSL runs with <c>StrictHostKeyChecking=yes</c>, so an unconfirmed host fails and a
///     changed key is always rejected.
/// </summary>
public class WslSshHostKeyTrust
{
    private const int DefaultSshPort = 22;
    private const string KnownHostsScript = "ssh-keygen -F \"$1\" >/dev/null";
    private const string ScanScript = "ssh-keyscan -T 10 -p \"$2\" -- \"$1\" 2>/dev/null";
    private const string FingerprintScript = "printf '%s\\n' \"$@\" | ssh-keygen -l -f -";

    private const string AppendScript =
        "mkdir -p ~/.ssh && chmod 700 ~/.ssh && printf '%s\\n' \"$@\" >> ~/.ssh/known_hosts && chmod 600 ~/.ssh/known_hosts";

    private readonly ILogger<WslSshHostKeyTrust> _logger;

    public WslSshHostKeyTrust(ILogger<WslSshHostKeyTrust> logger)
    {
        _logger = logger;
    }

    /// <summary>
    ///     Returns when <paramref name="destination" /> is in known_hosts or the user confirmed its keys.
    ///     An unreachable host is left to ssh, which then reports the connection error itself.
    /// </summary>
    /// <exception cref="CredentialsRequiredException">The host is unconfirmed during a scheduled run.</exception>
    /// <exception cref="OperationCanceledException">The user rejected the fingerprints.</exception>
    public virtual async Task EnsureTrustedAsync(string destination, int port, CancellationToken ct)
    {
        var host = HostName(destination);
        if ((await RunAsync(KnownHostsScript, [KnownHostsName(host, port)], ct)).ExitCode == 0)
            return;

        if (!OperationInteractivity.IsInteractive)
            throw new CredentialsRequiredException(
                string.Format(Strings.Get("Error.SshHostKeyUnconfirmed.Unattended"), host));

        var keys = Lines((await RunAsync(ScanScript, [host, port.ToString()], ct)).StandardOutput)
            .Where(line => !line.StartsWith('#'))
            .ToArray();
        if (keys.Length == 0)
        {
            _logger.LogWarning("ssh-keyscan returned no host keys for {Host}:{Port}", host, port);
            return;
        }

        var fingerprints = await RunAsync(FingerprintScript, keys, ct);
        if (fingerprints.ExitCode != 0)
            throw new InvalidOperationException($"ssh-keygen could not read the host keys of {host}.");

        if (!await ConfirmAsync(host, port, Lines(fingerprints.StandardOutput), ct))
            throw new OperationCanceledException(ct);

        var append = await RunAsync(AppendScript, keys, ct);
        if (append.ExitCode != 0)
            throw new InvalidOperationException($"Could not add the host keys of {host} to known_hosts in WSL.");
    }

    /// <summary>Builds the wsl.exe arguments for a constant script that receives values only as positional parameters.</summary>
    internal static IReadOnlyList<string> BuildArguments(string script, IEnumerable<string> parameters)
    {
        return ["--exec", "bash", "-c", script, "bash", .. parameters];
    }

    internal static string KnownHostsName(string host, int port)
    {
        return port is DefaultSshPort or <= 0 ? host : $"[{host}]:{port}";
    }

    internal static string HostName(string destination)
    {
        var host = destination[(destination.LastIndexOf('@') + 1)..];
        return host.StartsWith('[') && host.EndsWith(']') ? host[1..^1] : host;
    }

    protected virtual Task<BorgResult> RunAsync(string script, IEnumerable<string> parameters, CancellationToken ct)
    {
        return ProcessRunner.RunAsync("wsl", BuildArguments(script, parameters), ct);
    }

    protected virtual async Task<bool> ConfirmAsync(string host, int port, IReadOnlyList<string> fingerprints,
        CancellationToken ct)
    {
        if (Application.Current is null)
            return false;

        var message = string.Format(Strings.Get("SshHostKey.Confirm"),
            KnownHostsName(host, port), string.Join(Environment.NewLine, fingerprints));
        return await Dispatcher.UIThread.InvokeAsync(() => DialogHelper.ConfirmAsync(message, ct));
    }

    private static string[] Lines(string text)
    {
        return text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
