using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BorgMate.Models;
using Microsoft.Extensions.Logging;

namespace BorgMate.Services.Borg;

public abstract class BorgServiceBase(ILogger logger, AppSettings settings, SshAgentHelper sshAgent, WslHelper wsl) : IBorgService
{
    protected string BorgBinary => settings.EffectiveBorgPath;

    protected virtual async Task<BorgResult> RunCommandAsync(
        string fileName,
        string arguments,
        BorgEnv? env = null,
        string? workingDirectory = null,
        CancellationToken ct = default,
        Action<string>? onStderrLine = null)
    {
        var environment = env?.Variables;
        var wslPreCommands = env?.WslPreCommands;
        var logFileName = fileName;
        var logArguments = arguments;

        LogCommand(logFileName, logArguments, environment, workingDirectory);
        PopulateJobDebugInfo(logFileName, logArguments, environment);

        environment ??= new Dictionary<string, string>();
        environment["LC_ALL"] = "C";

        if (WslHelper.IsRequired && fileName != "wsl")
        {
            if (!wsl.IsAvailable)
                return new BorgResult(-1, "", Strings.Get("Error.WslNotAvailable"));

            var wslCwd = workingDirectory is not null ? WslHelper.ToWslPath(workingDirectory) : null;
            (fileName, arguments) = WslHelper.WrapCommand(fileName, arguments, environment, wslCwd, wslPreCommands);
            environment = null;
            workingDirectory = null;
        }

        // Stream stdout/stderr to log and to current BorgJob for real-time debug view.
        {
            var origStderr = onStderrLine;
            onStderrLine = line =>
            {
                origStderr?.Invoke(line);
                logger.LogDebug("  STDERR: {Line}", line);
                BorgJob.Current.Value?.AppendStderr(line);
            };
        }
        Action<string>? stdoutCallback = line =>
        {
            logger.LogDebug("  STDOUT: {Line}", line);
            BorgJob.Current.Value?.AppendStdout(line);
        };

        // On WSL, kill borg inside the Linux namespace before killing wsl.exe
        Action<System.Diagnostics.Process>? onKill = WslHelper.IsRequired ? _ =>
        {
            try { WslHelper.KillBorgProcesses(logFileName); }
            catch (Exception ex) { logger.LogWarning(ex, "Failed to kill borg process inside WSL"); }
        } : null;

        var result = await ProcessRunner.RunAsync(fileName, arguments, environment, workingDirectory, onStderrLine, ct, stdoutCallback, onKill: onKill);
        LogResult(logFileName, logArguments, result);
        return result;
    }

    private static void PopulateJobDebugInfo(string fileName, string arguments,
        Dictionary<string, string>? environment)
    {
        if (BorgJob.Current.Value is not { } job) return;
        job.CommandLine = $"{fileName} {arguments}";
        var safeEnv = environment?
            .Select(kv => kv.Key == "BORG_PASSPHRASE" ? $"{kv.Key}=***" : $"{kv.Key}={kv.Value}")
            .ToList();
        job.EnvironmentDisplay = safeEnv is { Count: > 0 } ? string.Join("\n", safeEnv) : null;
    }

    private void LogCommand(string fileName, string arguments,
        Dictionary<string, string>? environment, string? workingDirectory)
    {
        logger.LogInformation("Executing: {FileName} {Arguments}", fileName, arguments);
        if (workingDirectory is not null)
            logger.LogInformation("  CWD: {WorkingDirectory}", workingDirectory);
        var safeEnv = environment?
            .Select(kv => kv.Key == "BORG_PASSPHRASE" ? $"{kv.Key}=***" : $"{kv.Key}={kv.Value}")
            .ToList();
        if (safeEnv is { Count: > 0 })
            logger.LogInformation("  ENV: {Environment}", string.Join(", ", safeEnv));
    }

    private void LogResult(string fileName, string arguments, BorgResult result)
    {
        if (result.Success)
            logger.LogInformation("Completed: {FileName} {Arguments} (exit code: {ExitCode})",
                fileName, arguments, result.ExitCode);
        else
            logger.LogWarning("Failed: {FileName} {Arguments} (exit code: {ExitCode})",
                fileName, arguments, result.ExitCode);

        // stdout/stderr already logged line-by-line via streaming callbacks
    }

    /// <summary>
    /// Environment for borg process. WslPreCommands are shell commands prepended to the
    /// bash invocation on WSL (used for SSH_ASKPASS setup with encrypted keys).
    /// </summary>
    protected record BorgEnv(Dictionary<string, string> Variables, List<string>? WslPreCommands = null);

    /// <summary>
    /// Builds the process environment for a borg command: BORG_PASSPHRASE, BORG_RSH (for SSH repos),
    /// BORG_REMOTE_PATH, and loads SSH keys into the agent.
    /// </summary>
    protected async Task<BorgEnv> BuildEnvironmentAsync(BorgRepository repo)
    {
        var env = new Dictionary<string, string>();
        List<string>? wslPreCommands = null;

        // Auto-accept relocated repository warnings
        env["BORG_RELOCATED_REPO_ACCESS_IS_OK"] = "yes";

        if (!string.IsNullOrWhiteSpace(repo.Passphrase))
            env["BORG_PASSPHRASE"] = repo.Passphrase;

        if (!repo.IsLocal)
        {
            if (!string.IsNullOrWhiteSpace(repo.SshKeyPath))
                await sshAgent.EnsureKeyLoadedAsync(repo.SshKeyPath, repo);

            (env["BORG_RSH"], wslPreCommands) = await BuildBorgRshAsync(repo);
        }

        if (!string.IsNullOrWhiteSpace(repo.BorgRemotePath))
            env["BORG_REMOTE_PATH"] = repo.BorgRemotePath;

        return new BorgEnv(env, wslPreCommands);
    }

    /// <summary>
    /// Builds the BORG_RSH value and optional WSL pre-commands for SSH repos.
    /// </summary>
    private async Task<(string rsh, List<string>? wslPreCommands)> BuildBorgRshAsync(BorgRepository repo)
    {
        var rshParts = new List<string> { "ssh" };
        List<string>? wslPreCommands = null;

        if (!string.IsNullOrWhiteSpace(repo.SshKeyPath))
        {
            if (WslHelper.IsRequired)
            {
                var wslKey = await wsl.GetWslKeyPathAsync(repo.SshKeyPath);
                if (wslKey is not null)
                    rshParts.Add($"-i \"{wslKey}\"");

                wslPreCommands = BuildWslAskPassCommands(repo.SshKeyPassphrase);
            }
            else
            {
                rshParts.Add($"-i \"{P(repo.SshKeyPath)}\"");
            }
        }

        if (repo.SshPort != 22 && repo.SshPort > 0)
            rshParts.Add($"-p {repo.SshPort}");

        if (settings.SshKeepAliveInterval > 0)
        {
            rshParts.Add($"-o ServerAliveInterval={settings.SshKeepAliveInterval}");
            rshParts.Add($"-o ServerAliveCountMax={settings.SshKeepAliveCountMax}");
        }

        if (WslHelper.IsRequired)
            rshParts.Add("-o StrictHostKeyChecking=accept-new");

        return (string.Join(" ", rshParts), wslPreCommands);
    }

    /// <summary>
    /// Creates WSL pre-commands for SSH_ASKPASS with encrypted keys. Returns null if not needed.
    /// </summary>
    private List<string>? BuildWslAskPassCommands(string? sshKeyPassphrase)
    {
        if (sshKeyPassphrase is null)
            return null;

        var safePass = sshKeyPassphrase.Replace("'", "'\\''");
        logger.LogDebug("WSL pre-commands: SSH_ASKPASS for encrypted key");
        return
        [
            "_BORGMATE_ASKPASS=$(mktemp -p \"${TMPDIR:-/tmp}\" .borgmate-askpass-XXXXXX)",
            "trap 'rm -f \"$_BORGMATE_ASKPASS\"' EXIT",
            "chmod 700 \"$_BORGMATE_ASKPASS\"",
            $"printf '#!/bin/sh\\necho '\"'\"'{safePass}'\"'\"'\\n' > \"$_BORGMATE_ASKPASS\"",
            "export SSH_ASKPASS=\"$_BORGMATE_ASKPASS\"",
            "export SSH_ASKPASS_REQUIRE=force",
            "export DISPLAY=:0"
        ];
    }

    protected Dictionary<string, string> BuildEnvironment(string? passphrase, string? remotePath)
    {
        var env = new Dictionary<string, string>();

        if (!string.IsNullOrWhiteSpace(passphrase))
            env["BORG_PASSPHRASE"] = passphrase;

        if (!string.IsNullOrWhiteSpace(remotePath))
            env["BORG_REMOTE_PATH"] = remotePath;

        return env;
    }

    public Task<BorgResult> GetVersionAsync(CancellationToken ct = default)
    {
        return RunCommandAsync(BorgBinary, "--version", ct: ct);
    }

    public Task<BorgResult> CheckRemotePathAsync(
        string host, string remotePath,
        string? sshKeyPath = null, CancellationToken ct = default)
    {
        var keyArg = string.IsNullOrWhiteSpace(sshKeyPath) ? "" : $"-i \"{sshKeyPath}\" ";
        var args = $"{keyArg}\"{host}\" which \"{remotePath}\"";
        return RunCommandAsync("ssh", args, ct: ct);
    }

    /// <summary>
    /// Converts a local path for WSL if needed (no-op on macOS/Linux).
    /// </summary>
    protected static string P(string path) =>
        WslHelper.IsRequired ? WslHelper.ToWslPath(path) : path;

    /// <summary>
    /// Builds `--keep-*` retention flags from a repo's prune options. Returns an
    /// empty string when no retention rule is set; callers must validate that
    /// at least one rule is set before invoking prune (borg errors otherwise).
    /// </summary>
    protected static string BuildPruneRetentionArgs(PruneOptions p)
    {
        var parts = new List<string>();
        if (p.KeepLast > 0) parts.Add($"--keep-last {p.KeepLast}");
        if (p.KeepHourly > 0) parts.Add($"--keep-hourly {p.KeepHourly}");
        if (p.KeepDaily > 0) parts.Add($"--keep-daily {p.KeepDaily}");
        if (p.KeepWeekly > 0) parts.Add($"--keep-weekly {p.KeepWeekly}");
        if (p.KeepMonthly > 0) parts.Add($"--keep-monthly {p.KeepMonthly}");
        if (p.KeepYearly > 0) parts.Add($"--keep-yearly {p.KeepYearly}");
        return parts.Count == 0 ? "" : " " + string.Join(" ", parts);
    }

    // Version-specific operations implemented by subclasses
    public abstract Task<BorgResult> InitAsync(
        BorgRepository repo,
        CancellationToken ct = default);

    public abstract Task<BorgResult> CreateBackupAsync(
        BorgRepository repo, string archiveName,
        IEnumerable<string> sourcePaths, CancellationToken ct = default,
        Action<string>? onStderrLine = null);

    public abstract Task<BorgResult> ListArchivesAsync(
        BorgRepository repo, CancellationToken ct = default);

    public abstract Task<BorgResult> InfoArchiveAsync(
        BorgRepository repo, string archiveName, CancellationToken ct = default);

    public abstract Task<BorgResult> ListArchiveContentsAsync(
        BorgRepository repo, string archiveName,
        CancellationToken ct = default);

    public abstract Task<BorgResult> ExtractAsync(
        BorgRepository repo, string archiveName,
        string restorePath, IReadOnlyList<string>? paths = null,
        CancellationToken ct = default,
        Action<string>? onStderrLine = null);

    public abstract Task<BorgResult> DeleteArchiveAsync(
        BorgRepository repo, string archiveName,
        CancellationToken ct = default);

    public abstract Task<BorgResult> PruneAsync(
        BorgRepository repo,
        CancellationToken ct = default);

    public abstract Task<BorgResult> CheckAsync(
        BorgRepository repo,
        CancellationToken ct = default,
        Action<string>? onStderrLine = null);

    public abstract Task<BorgResult> CompactAsync(
        BorgRepository repo,
        CancellationToken ct = default,
        Action<string>? onStderrLine = null);

    public abstract Task<BorgResult> InfoRepoAsync(
        BorgRepository repo,
        CancellationToken ct = default);

    public abstract Task<BorgResult> DiffArchivesAsync(
        BorgRepository repo, string archive1, string archive2,
        CancellationToken ct = default);
}
