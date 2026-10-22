using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BorgMate.Domain.Repositories;
using BorgMate.Domain.Settings;
using BorgMate.Infrastructure.Platform;
using BorgMate.Runtime.Jobs;
using BorgMate.Runtime.Security;
using Microsoft.Extensions.Logging;
using WslHelper = BorgMate.Infrastructure.Platform.WslHelper;

namespace BorgMate.Infrastructure.Borg;

public abstract class BorgServiceBase : IBorgService
{
    private readonly ILogger logger;
    private readonly AppSettings settings;
    private readonly BorgVersion borgVersion;
    private readonly WslHelper wsl;
    private readonly INetworkShareMounter shareMounter;
    private readonly PromptingPassphraseProvider passphrase;

    protected BorgServiceBase(
        ILogger logger,
        AppSettings settings,
        BorgVersion borgVersion,
        SshRepositoryTransport sshTransport,
        WslHelper wsl,
        INetworkShareMounter shareMounter,
        PromptingPassphraseProvider passphrase)
    {
        this.logger = logger;
        this.settings = settings;
        this.borgVersion = borgVersion;
        this.wsl = wsl;
        this.shareMounter = shareMounter;
        this.passphrase = passphrase;
        SshTransport = sshTransport;
    }

    protected SshRepositoryTransport SshTransport { get; }

    protected string BorgBinary => settings.GetEffectiveBorgPath(borgVersion);

    public Task<BorgResult> GetVersionAsync(CancellationToken ct = default)
    {
        return RunCommandAsync(BorgBinary, "--version", ct: ct);
    }

    public abstract Task<BorgCliVersionValidationResult> ValidateCliVersionAsync(CancellationToken ct = default);

    public abstract void UpdateProgress(BorgJob job, string line);

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
        string restorePath, BorgExtractOptions extractOptions,
        IReadOnlyList<string>? paths = null,
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

    public abstract Task<BorgResult> ChangePassphraseAsync(
        BorgRepository repo, string newPassphrase,
        CancellationToken ct = default);

    public abstract Task<BorgResult> InfoRepoAsync(
        BorgRepository repo,
        CancellationToken ct = default);

    public abstract Task<BorgResult> DiffArchivesAsync(
        BorgRepository repo, string archive1, string archive2,
        CancellationToken ct = default);

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
        ApplyDefaultEnvironment(environment);

        if (WslHelper.IsRequired && fileName != "wsl")
        {
            if (!await wsl.IsAvailableAsync(ct))
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
        Action<Process>? onKill = WslHelper.IsRequired
            ? _ =>
            {
                try
                {
                    WslHelper.KillProcesses(logFileName);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to kill borg process inside WSL");
                }
            }
            : null;

        var result = await ProcessRunner.RunAsync(fileName, arguments, environment, workingDirectory, onStderrLine, ct,
            stdoutCallback, onKill);
        LogResult(logFileName, logArguments, result);
        return result;
    }

    internal static void ApplyDefaultEnvironment(Dictionary<string, string> environment)
    {
        environment["LC_ALL"] = "C";
        environment["BORG_EXIT_CODES"] = "modern";
    }

    private static bool IsSecretEnv(string key)
    {
        return key is "BORG_PASSPHRASE" or "BORG_NEW_PASSPHRASE";
    }

    private static string SanitizeForDisplay(string value)
    {
        return BorgCommandRedactor.Redact(value);
    }

    private static void PopulateJobDebugInfo(string fileName, string arguments,
        Dictionary<string, string>? environment)
    {
        if (BorgJob.Current.Value is not { } job) return;
        job.CommandLine = $"{fileName} {SanitizeForDisplay(arguments)}";
        var safeEnv = environment?
            .Select(kv => IsSecretEnv(kv.Key) ? $"{kv.Key}=***" : $"{kv.Key}={SanitizeForDisplay(kv.Value)}")
            .ToList();
        job.EnvironmentDisplay = safeEnv is { Count: > 0 } ? string.Join("\n", safeEnv) : null;
    }

    private void LogCommand(string fileName, string arguments,
        Dictionary<string, string>? environment, string? workingDirectory)
    {
        logger.LogInformation("Executing: {FileName} {Arguments}", fileName, SanitizeForDisplay(arguments));
        if (workingDirectory is not null)
            logger.LogInformation("  CWD: {WorkingDirectory}", workingDirectory);
        var safeEnv = environment?
            .Select(kv => IsSecretEnv(kv.Key) ? $"{kv.Key}=***" : $"{kv.Key}={SanitizeForDisplay(kv.Value)}")
            .ToList();
        if (safeEnv is { Count: > 0 })
            logger.LogInformation("  ENV: {Environment}", string.Join(", ", safeEnv));
    }

    private void LogResult(string fileName, string arguments, BorgResult result)
    {
        if (result.Success)
            logger.LogInformation("Completed: {FileName} {Arguments} (exit code: {ExitCode})",
                fileName, SanitizeForDisplay(arguments), result.ExitCode);
        else
            logger.LogWarning("Failed: {FileName} {Arguments} (exit code: {ExitCode})",
                fileName, SanitizeForDisplay(arguments), result.ExitCode);

        // stdout/stderr already logged line-by-line via streaming callbacks
    }

    /// <summary>
    ///     Builds the process environment for a borg command: BORG_PASSPHRASE, BORG_RSH (for SSH repos),
    ///     BORG_REMOTE_PATH, and loads SSH keys into the agent. When <paramref name="newPassphrase" />
    ///     is non-empty, BORG_NEW_PASSPHRASE is set (used by <c>borg key change-passphrase</c>).
    /// </summary>
    protected async Task<BorgEnv> BuildEnvironmentAsync(
        BorgRepository repo,
        string? newPassphrase = null,
        CancellationToken ct = default)
    {
        var env = new Dictionary<string, string>();
        List<string>? wslPreCommands = null;

        // Auto-accept relocated repository warnings
        env["BORG_RELOCATED_REPO_ACCESS_IS_OK"] = "yes";

        var currentPassphrase = passphrase.GetCachedPassphrase(repo);
        if (!string.IsNullOrWhiteSpace(currentPassphrase))
            env["BORG_PASSPHRASE"] = currentPassphrase;

        if (!string.IsNullOrEmpty(newPassphrase))
            env["BORG_NEW_PASSPHRASE"] = newPassphrase;

        if (repo.LocationKind == BorgRepositoryLocationKind.SmbShare)
        {
            // Borg 1.x cannot fsync directories on WSL 1 drvfs SMB mounts (EOPNOTSUPP),
            // so the combination is rejected up front instead of failing mid-backup.
            if (borgVersion == BorgVersion.Borg1 && WslHelper.IsRequired && !await wsl.IsWsl2Async(ct))
                throw new NetworkShareMountException(Strings.Get("Error.SmbBorg1Wsl1Unsupported"));

            await shareMounter.EnsureMountedAsync(ParseSmbLocation(repo), ct);
        }

        if (SshTransport.UsesSsh(repo))
        {
            var preparedSsh = await SshTransport.PrepareForBorgAsync(repo, ct);
            env["BORG_RSH"] = preparedSsh.BorgRsh;
            wslPreCommands = preparedSsh.WslPreCommands;
        }

        if (!string.IsNullOrWhiteSpace(repo.BorgRemotePath))
            env["BORG_REMOTE_PATH"] = repo.BorgRemotePath;

        return new BorgEnv(env, wslPreCommands);
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

    /// <summary>
    ///     Converts a local path for WSL if needed (no-op on macOS/Linux).
    /// </summary>
    protected static string P(string path)
    {
        return WslHelper.IsRequired ? WslHelper.ToWslPath(path) : path;
    }

    /// <summary>
    ///     Repository location for borg CLI arguments: the configured path (WSL-converted on
    ///     Windows), or the resolved mountpoint path for SMB repositories, which is already
    ///     WSL-native on Windows and must not be converted again.
    /// </summary>
    protected string Location(BorgRepository repo)
    {
        return repo.LocationKind == BorgRepositoryLocationKind.SmbShare
            ? shareMounter.GetMountedPath(ParseSmbLocation(repo))
            : P(repo.Path);
    }

    private static SmbLocation ParseSmbLocation(BorgRepository repo)
    {
        return SmbLocation.TryParse(repo.Path)
               ?? throw new NetworkShareMountException(
                   string.Format(Strings.Get("Error.SmbInvalidUrl"), repo.Path));
    }

    /// <summary>
    ///     Builds `--keep-*` retention flags from a repo's prune options. Returns an
    ///     empty string when no retention rule is set; callers must validate that
    ///     at least one rule is set before invoking prune (borg errors otherwise).
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

    /// <summary>
    ///     Lock wait for <c>borg create</c>. After a dropped connection the server-side borg keeps
    ///     the repository lock until the server notices the dead client, which outlasts borg's
    ///     one-second default and the retry delays.
    /// </summary>
    protected static string BackupLockWaitArgs => $" --lock-wait {(int)BackupLockWait.TotalSeconds}";

    private static readonly TimeSpan BackupLockWait = TimeSpan.FromMinutes(10);

    protected static string BuildExcludeArgs(IEnumerable<string> excludedPaths)
    {
        return string.Concat(excludedPaths.Select(path => $" --exclude \"pp:{P(path)}\""));
    }

    /// <summary>Builds optional metadata flags for <c>borg extract</c>.</summary>
    protected static string BuildExtractOptionArgs(BorgExtractOptions options)
    {
        var parts = new List<string>();
        if (options.NoFlags) parts.Add("--noflags");
        if (options.NoAcls) parts.Add("--noacls");
        if (options.NoXattrs) parts.Add("--noxattrs");
        if (options.Sparse) parts.Add("--sparse");
        return parts.Count == 0 ? "" : " " + string.Join(" ", parts);
    }

    /// <summary>
    ///     Environment for borg process. WslPreCommands are shell commands prepended to the
    ///     bash invocation on WSL (used for SSH_ASKPASS setup with encrypted keys).
    /// </summary>
    protected record BorgEnv(Dictionary<string, string> Variables, List<string>? WslPreCommands = null);

    protected async Task<BorgCliVersionValidationResult> ParseCliVersionAsync(CancellationToken ct)
    {
        var versionResult = await GetVersionAsync(ct);
        if (!versionResult.Success)
            return BorgCliVersionValidationResult.Failure(versionResult);

        var version = BorgCliVersion.Parse(versionResult.StandardOutput);
        return version is null
            ? BorgCliVersionValidationResult.Validation("Validation.BorgVersionUnknown")
            : new BorgCliVersionValidationResult(version);
    }

    public async Task<bool> SupportsFeatureAsync(BorgCliFeature feature, CancellationToken ct = default)
    {
        var version = await GetCliVersionCachedAsync(ct);
        return version?.Supports(feature) == true;
    }

    private (string Path, BorgCliVersion Version)? _cliVersionCache;

    /// <summary>
    ///     Parsed version of the effective borg binary, cached per binary path so
    ///     version-gated CLI flags don't spawn <c>borg --version</c> on every command.
    ///     Returns null (and does not cache) when the binary fails or is unparsable.
    ///     A borg upgraded in place at the same path is picked up on the next app launch.
    /// </summary>
    private async Task<BorgCliVersion?> GetCliVersionCachedAsync(CancellationToken ct)
    {
        var path = BorgBinary;
        if (_cliVersionCache is { } cached && cached.Path == path)
            return cached.Version;

        var version = (await ParseCliVersionAsync(ct)).Version;
        if (version is not null)
            _cliVersionCache = (path, version);
        return version;
    }
}
