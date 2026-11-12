using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BorgMate.Domain.Repositories;
using BorgMate.Domain.Settings;
using BorgMate.Infrastructure.Borg;
using Microsoft.Extensions.Logging;

namespace BorgMate.Infrastructure.Platform;

/// <summary>Prepares repository SSH connections and executes safe remote commands.</summary>
public class SshRepositoryTransport
{
    /// <summary>Carries the WSL key passphrase to the SSH_ASKPASS script.</summary>
    public const string WslKeyPassphraseVariable = "BORGMATE_SSH_KEY_PASSPHRASE";

    private readonly AppSettings _settings;
    private readonly SshAgentHelper _sshAgent;
    private readonly WslHelper _wsl;
    private readonly WslSshHostKeyTrust _hostKeys;
    private readonly ILogger<SshRepositoryTransport> _logger;

    public SshRepositoryTransport(
        AppSettings settings,
        SshAgentHelper sshAgent,
        WslHelper wsl,
        WslSshHostKeyTrust hostKeys,
        ILogger<SshRepositoryTransport> logger)
    {
        _settings = settings;
        _sshAgent = sshAgent;
        _wsl = wsl;
        _hostKeys = hostKeys;
        _logger = logger;
    }

    public virtual bool UsesSsh(BorgRepository repository)
    {
        return repository.IsSsh ||
               repository.LocationKind == BorgRepositoryLocationKind.BorgUrl &&
               BorgRepositoryUrlValidator.UsesSshTransport(RepositoryUrl(repository));
    }

    public virtual SshRepositoryTarget ResolveTarget(BorgRepository repository)
    {
        var location = repository.LocationKind == BorgRepositoryLocationKind.BorgUrl
            ? RepositoryUrl(repository)
            : repository.Path;

        if (Uri.TryCreate(location, UriKind.Absolute, out var uri) && uri.Scheme is "ssh" or "sftp")
        {
            var user = Uri.UnescapeDataString(uri.UserInfo);
            var host = user.Length == 0 ? uri.Host : $"{user}@{uri.Host}";
            var authorityEnd = location.IndexOf("//", StringComparison.Ordinal) + 2;
            var pathStart = location.IndexOf('/', authorityEnd);
            var pathEnd = pathStart < 0 ? -1 : location.IndexOfAny(['?', '#'], pathStart);
            var rawPath = pathStart < 0
                ? string.Empty
                : location[pathStart..(pathEnd < 0 ? location.Length : pathEnd)];
            var path = Uri.UnescapeDataString(rawPath);
            if (path.StartsWith("/./", StringComparison.Ordinal))
                path = path[3..];

            return CreateTarget(host, path, uri.Port > 0 ? uri.Port : 22);
        }

        // Borg parses an unparseable ssh:// location itself; the scp-style split below would miss its host.
        if (location.Contains("://", StringComparison.Ordinal))
            throw new ArgumentException("Invalid SSH repository URL.", nameof(repository));

        var colonIndex = SshDestination.HostEndIndex(location);
        var legacyHost = colonIndex < 0 ? location : location[..colonIndex];
        var legacyPath = colonIndex < 0 ? string.Empty : location[(colonIndex + 1)..];
        return CreateTarget(legacyHost, legacyPath, repository.SshPort);
    }

    /// <summary>Prepares the ssh command borg runs, after checking the destination borg will pass to it.</summary>
    public Task<PreparedSshConnection> PrepareForBorgAsync(
        BorgRepository repository,
        CancellationToken ct = default)
    {
        var target = ResolveTarget(repository);
        // A Borg URL carries its own port, so ssh must not get another -p.
        var sshPort = repository.LocationKind == BorgRepositoryLocationKind.BorgUrl ? 22 : repository.SshPort;
        return PrepareAsync(repository, target, sshPort, ct);
    }

    public virtual async Task PrepareKeyAsync(
        BorgRepository repository,
        string sshKeyPath,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (_wsl.RequiresWsl && UsesSsh(repository) && !string.IsNullOrWhiteSpace(sshKeyPath))
            _ = await _wsl.CopySshKeyAsync(sshKeyPath, ct)
                ?? throw new FileNotFoundException("Failed to copy the SSH key into WSL.", sshKeyPath);
    }

    public virtual async Task<BorgResult> RunAsync(
        BorgRepository repository,
        SshRepositoryTarget target,
        string remoteCommand,
        CancellationToken ct = default)
    {
        ValidateHost(target.Host);
        var prepared = await PrepareAsync(repository, target, target.Port, ct);
        var arguments = BuildCommandArguments(prepared, target, remoteCommand, _wsl.RequiresWsl);

        _logger.LogDebug("Executing SSH command for {Host}: {Command}", target.Host,
            BorgCommandRedactor.Redact(remoteCommand));

        if (!_wsl.RequiresWsl)
            return await ProcessRunner.RunAsync("ssh", arguments, ct);

        var jobId = WslHelper.NewJobId();
        var variables = new Dictionary<string, string>(prepared.WslEnvironment ?? new Dictionary<string, string>())
        {
            [WslHelper.JobIdVariable] = jobId
        };
        return await ProcessRunner.RunAsync("wsl", BuildWslCommandArguments(arguments, prepared.WslPreCommands), ct,
            _ => WslHelper.KillJob(jobId), WslHelper.ForwardEnvironment(variables));
    }

    public static string QuoteRemoteArgument(string value)
    {
        return $"'{value.Replace("'", "'\"'\"'")}'";
    }

    internal static List<string> BuildCommandArguments(
        PreparedSshConnection prepared,
        SshRepositoryTarget target,
        string remoteCommand,
        bool requiresWsl)
    {
        var arguments = new List<string>(prepared.Arguments)
        {
            "-o", "ConnectTimeout=10"
        };

        if (requiresWsl)
        {
            if (!arguments.Contains("PasswordAuthentication=no", StringComparer.Ordinal))
                arguments.AddRange(["-o", "PasswordAuthentication=no"]);
            if (!arguments.Contains("KbdInteractiveAuthentication=no", StringComparer.Ordinal))
                arguments.AddRange(["-o", "KbdInteractiveAuthentication=no"]);
        }
        else
            arguments.AddRange(["-o", "BatchMode=yes"]);

        arguments.Add("--");
        arguments.Add(target.Host);
        arguments.Add(remoteCommand);
        return arguments;
    }

    internal static IReadOnlyList<string> BuildWslCommandArguments(
        IReadOnlyList<string> sshArguments,
        IReadOnlyList<string>? preCommands)
    {
        var commands = new List<string> { "cd ~" };
        if (preCommands is { Count: > 0 })
            commands.AddRange(preCommands);
        commands.Add($"ssh {string.Join(' ', sshArguments.Select(WslHelper.ShellEscapePublic))}");
        // "--" would hand the joined command line to the default shell, which expands $variables
        // before bash -c runs; --exec passes the script to bash untouched.
        return ["--exec", "bash", "-c", string.Join(" && ", commands)];
    }

    /// <param name="port">Port for ssh's <c>-p</c>; 22 omits it.</param>
    private async Task<PreparedSshConnection> PrepareAsync(
        BorgRepository repository,
        SshRepositoryTarget target,
        int port,
        CancellationToken ct)
    {
        if (_wsl.RequiresWsl)
            await _hostKeys.EnsureTrustedAsync(target.Host, target.Port, ct);

        if (!string.IsNullOrWhiteSpace(repository.SshKeyPath) &&
            !await _sshAgent.EnsureKeyLoadedAsync(repository.SshKeyPath, repository, ct))
            throw new OperationCanceledException(ct);

        var arguments = new List<string>();
        List<string>? wslPreCommands = null;
        Dictionary<string, string>? wslEnvironment = null;
        if (!string.IsNullOrWhiteSpace(repository.SshKeyPath))
        {
            var keyPath = repository.SshKeyPath;
            if (_wsl.RequiresWsl)
            {
                keyPath = await _wsl.GetWslKeyPathAsync(repository.SshKeyPath, ct)
                          ?? throw new FileNotFoundException(
                              "The copied SSH key is not available inside WSL.",
                              repository.SshKeyPath);
                if (_sshAgent.GetWslKeyPassphrase(repository.SshKeyPath) is { } keyPassphrase)
                {
                    wslPreCommands = BuildWslAskPassCommands();
                    wslEnvironment = new Dictionary<string, string> { [WslKeyPassphraseVariable] = keyPassphrase };
                }
            }

            arguments.AddRange(["-i", keyPath]);
        }

        if (port is > 0 and not 22)
            arguments.AddRange(["-p", port.ToString()]);

        if (_settings.SshKeepAliveInterval > 0)
        {
            arguments.AddRange(["-o", $"ServerAliveInterval={_settings.SshKeepAliveInterval}"]);
            arguments.AddRange(["-o", $"ServerAliveCountMax={_settings.SshKeepAliveCountMax}"]);
        }

        if (_wsl.RequiresWsl)
        {
            arguments.AddRange(["-o", "StrictHostKeyChecking=yes"]);
            arguments.AddRange(["-o", "PasswordAuthentication=no", "-o", "KbdInteractiveAuthentication=no"]);
        }

        var borgRsh = string.Join(' ', new[] { "ssh" }.Concat(arguments.Select(QuoteRemoteArgument)));
        return new PreparedSshConnection
        {
            Arguments = arguments,
            BorgRsh = borgRsh,
            WslPreCommands = wslPreCommands,
            WslEnvironment = wslEnvironment
        };
    }

    /// <summary>
    ///     Points SSH_ASKPASS at a throwaway script that prints <see cref="WslKeyPassphraseVariable" />.
    ///     The script holds no secret: ssh hands its own environment to the askpass child.
    /// </summary>
    internal static List<string> BuildWslAskPassCommands()
    {
        return
        [
            "_BORGMATE_ASKPASS=$(mktemp -p \"${TMPDIR:-/tmp}\" .borgmate-askpass-XXXXXX)",
            "trap 'rm -f \"$_BORGMATE_ASKPASS\"' EXIT",
            "chmod 700 \"$_BORGMATE_ASKPASS\"",
            $"printf '#!/bin/sh\\nprintf \"%%s\\\\n\" \"${WslKeyPassphraseVariable}\"\\n' > \"$_BORGMATE_ASKPASS\"",
            "export SSH_ASKPASS=\"$_BORGMATE_ASKPASS\"",
            "export SSH_ASKPASS_REQUIRE=force",
            "export DISPLAY=:0"
        ];
    }

    private static SshRepositoryTarget CreateTarget(string host, string path, int port)
    {
        ValidateHost(host);
        return new SshRepositoryTarget
        {
            Host = host,
            RepositoryPath = path.Length == 0 ? "." : path.StartsWith("-", StringComparison.Ordinal) ? $"./{path}" : path,
            Port = port > 0 ? port : 22
        };
    }

    private static void ValidateHost(string host)
    {
        if (!SshDestination.IsValid(host))
            throw new ArgumentException("Invalid SSH host.", nameof(host));
    }

    private static string RepositoryUrl(BorgRepository repository)
    {
        return string.IsNullOrWhiteSpace(repository.BorgUrl) ? repository.Path : repository.BorgUrl;
    }
}
