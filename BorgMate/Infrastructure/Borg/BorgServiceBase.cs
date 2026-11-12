using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
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
    private const string WslCommand = "wsl";

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
        return RunCommandAsync(BorgBinary, ["--version"], ct: ct);
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

    public abstract Task<BorgResult> AcceptRelocationAsync(
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
        IReadOnlyList<string> arguments,
        BorgEnv? env = null,
        string? workingDirectory = null,
        CancellationToken ct = default,
        Action<string>? onStderrLine = null)
    {
        var environment = env?.Variables;
        var wslPreCommands = env?.WslPreCommands;
        var logFileName = fileName;
        var logArguments = FormatArgumentsForDisplay(arguments);

        LogCommand(logFileName, logArguments, environment, workingDirectory);
        PopulateJobDebugInfo(logFileName, logArguments, environment);

        environment ??= new Dictionary<string, string>();
        ApplyDefaultEnvironment(environment);

        string? wslJobId = null;
        if (WslHelper.IsRequired && fileName != WslCommand)
        {
            if (!await wsl.IsAvailableAsync(ct))
                return new BorgResult(-1, "", Strings.Get("Error.WslNotAvailable"));

            wslJobId = WslHelper.NewJobId();
            environment[WslHelper.JobIdVariable] = wslJobId;

            var wslCwd = workingDirectory is not null ? WslHelper.ToWslPath(workingDirectory) : null;
            arguments = WslHelper.WrapCommand(fileName, arguments, wslCwd, wslPreCommands);
            fileName = WslCommand;
            environment = WslHelper.ForwardEnvironment(environment);
            workingDirectory = null;
        }

        // Stream stderr to the log and both streams to the current BorgJob for the real-time debug view.
        // Stdout stays out of the log: it carries archive and file listings.
        {
            var origStderr = onStderrLine;
            onStderrLine = line =>
            {
                origStderr?.Invoke(line);
                logger.LogDebug("  STDERR: {Line}", line);
                BorgJob.Current.Value?.AppendStderr(line);
            };
        }
        Action<string>? stdoutCallback = line => BorgJob.Current.Value?.AppendStdout(line);

        // On WSL, kill borg inside the Linux namespace before killing wsl.exe
        Action<Process>? onKill = wslJobId is not null
            ? _ =>
            {
                try
                {
                    WslHelper.KillJob(wslJobId);
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

    private static bool IsSecretEnv(string key, string value)
    {
        return key is "BORG_PASSPHRASE" or "BORG_NEW_PASSPHRASE" or "PASSWD"
                   or SshRepositoryTransport.WslKeyPassphraseVariable or SshAgentHelper.ExpectPassphraseVariable &&
               value.Length > 0;
    }

    private static string SanitizeForDisplay(string value)
    {
        return BorgCommandRedactor.Redact(value);
    }

    /// <summary>Renders argv for logs and the debug view only; the process receives the elements unchanged.</summary>
    private static string FormatArgumentsForDisplay(IEnumerable<string> arguments)
    {
        return string.Join(' ', arguments.Select(argument =>
        {
            var redacted = SanitizeForDisplay(argument);
            return redacted.Length == 0 || redacted.Any(c => char.IsWhiteSpace(c) || c is '"' or '\\')
                ? $"\"{redacted.Replace("\"", "\\\"")}\""
                : redacted;
        }));
    }

    private static void PopulateJobDebugInfo(string fileName, string arguments,
        Dictionary<string, string>? environment)
    {
        if (BorgJob.Current.Value is not { } job) return;
        job.CommandLine = $"{fileName} {arguments}";
        var safeEnv = environment?
            .Select(kv => IsSecretEnv(kv.Key, kv.Value) ? $"{kv.Key}=***" : $"{kv.Key}={SanitizeForDisplay(kv.Value)}")
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
            .Select(kv => IsSecretEnv(kv.Key, kv.Value) ? $"{kv.Key}=***" : $"{kv.Key}={SanitizeForDisplay(kv.Value)}")
            .ToList();
        if (safeEnv is { Count: > 0 })
            logger.LogInformation("  ENV: {Environment}", string.Join(", ", safeEnv));
    }

    private void LogResult(string fileName, string arguments, BorgResult result)
    {
        if (result.Success)
            logger.LogInformation("Completed: {FileName} {Arguments} (exit code: {ExitCode}, stdout: {StdoutLength} chars)",
                fileName, arguments, result.ExitCode, result.StandardOutput.Length);
        else
            logger.LogWarning("Failed: {FileName} {Arguments} (exit code: {ExitCode}, stdout: {StdoutLength} chars)",
                fileName, arguments, result.ExitCode, result.StandardOutput.Length);
    }

    /// <summary>
    ///     Builds the process environment for a borg command: BORG_PASSPHRASE, BORG_RSH (for SSH repos),
    ///     BORG_REMOTE_PATH, and loads SSH keys into the agent. BORG_PASSPHRASE is empty when no passphrase
    ///     is cached for <paramref name="repo" />. When <paramref name="newPassphrase" />
    ///     is non-empty, BORG_NEW_PASSPHRASE is set (used by <c>borg key change-passphrase</c>).
    ///     Borg's relocated-repository prompt is answered "yes" only when <paramref name="acceptRelocation" /> is set.
    /// </summary>
    protected async Task<BorgEnv> BuildEnvironmentAsync(
        BorgRepository repo,
        string? newPassphrase = null,
        CancellationToken ct = default,
        bool acceptRelocation = false)
    {
        var env = new Dictionary<string, string>();
        List<string>? wslPreCommands = null;

        // Answering explicitly keeps the relocation decision independent of how borg reads stdin.
        env["BORG_RELOCATED_REPO_ACCESS_IS_OK"] = acceptRelocation ? "yes" : "no";

        // With BORG_PASSPHRASE unset, borg asks for the passphrase on the terminal, which WSL always
        // provides, and the command hangs. An empty passphrase makes borg exit with PassphraseWrong.
        var currentPassphrase = passphrase.GetCachedPassphrase(repo);
        env["BORG_PASSPHRASE"] = string.IsNullOrWhiteSpace(currentPassphrase) ? string.Empty : currentPassphrase;

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
            if (preparedSsh.WslEnvironment is not null)
                foreach (var (key, value) in preparedSsh.WslEnvironment)
                    env[key] = value;
        }

        if (!string.IsNullOrWhiteSpace(repo.BorgRemotePath))
            env["BORG_REMOTE_PATH"] = repo.BorgRemotePath;

        return new BorgEnv(env, wslPreCommands);
    }

    /// <summary>
    ///     Builds the environment for creating <paramref name="repo" />. An encrypted repository needs a
    ///     cached passphrase: borg would accept the empty BORG_PASSPHRASE as the new repository's passphrase.
    /// </summary>
    protected Task<BorgEnv> BuildInitEnvironmentAsync(BorgRepository repo, CancellationToken ct)
    {
        if (repo.IsEncrypted && string.IsNullOrWhiteSpace(passphrase.GetCachedPassphrase(repo)))
            throw new InvalidOperationException($"No passphrase is cached to initialize repository '{repo.Name}'.");

        return BuildEnvironmentAsync(repo, ct: ct);
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

    /// <summary>Borg 1.x <c>REPO::ARCHIVE</c> argument as a single argv element.</summary>
    protected string ArchiveLocation(BorgRepository repo, string archiveName)
    {
        return $"{Location(repo)}::{archiveName}";
    }

    /// <summary>
    ///     Addresses an archive by the exact name borg reported. Borg 1 expands <c>{placeholders}</c> in
    ///     <c>REPO::ARCHIVE</c>, so braces in such a name are doubled to keep them literal.
    /// </summary>
    protected string ExistingArchiveLocation(BorgRepository repo, string archiveName)
    {
        return ArchiveLocation(repo, archiveName.Replace("{", "{{").Replace("}", "}}"));
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
    protected static List<string> BuildPruneRetentionArgs(PruneOptions p)
    {
        var args = new List<string>();
        AddRetention(args, "--keep-last", p.KeepLast);
        AddRetention(args, "--keep-hourly", p.KeepHourly);
        AddRetention(args, "--keep-daily", p.KeepDaily);
        AddRetention(args, "--keep-weekly", p.KeepWeekly);
        AddRetention(args, "--keep-monthly", p.KeepMonthly);
        AddRetention(args, "--keep-yearly", p.KeepYearly);
        return args;
    }

    private static void AddRetention(List<string> args, string option, int count)
    {
        if (count > 0) args.AddRange([option, count.ToString(CultureInfo.InvariantCulture)]);
    }

    /// <summary>
    ///     Lock wait for <c>borg create</c>. After a dropped connection the server-side borg keeps
    ///     the repository lock until the server notices the dead client, which outlasts borg's
    ///     one-second default and the retry delays.
    /// </summary>
    protected static string[] BackupLockWaitArgs =>
        ["--lock-wait", ((int)BackupLockWait.TotalSeconds).ToString(CultureInfo.InvariantCulture)];

    private static readonly TimeSpan BackupLockWait = TimeSpan.FromMinutes(10);

    protected static IEnumerable<string> BuildExcludeArgs(IEnumerable<string> excludedPaths)
    {
        return excludedPaths.SelectMany(path => new[] { "--exclude", $"pp:{P(path)}" });
    }

    /// <summary>
    ///     Marks archive paths as literal path prefixes. Borg reads a leading <c>re:</c>, <c>sh:</c> or
    ///     similar as a pattern style, so an archive entry named like one would select other files.
    /// </summary>
    protected static IEnumerable<string> BuildExtractPathArgs(IEnumerable<string>? archivePaths)
    {
        return (archivePaths ?? []).Select(path => $"pp:{path}");
    }

    /// <summary>Builds optional metadata flags for <c>borg extract</c>.</summary>
    protected static List<string> BuildExtractOptionArgs(BorgExtractOptions options)
    {
        var args = new List<string>();
        if (options.NoFlags) args.Add("--noflags");
        if (options.NoAcls) args.Add("--noacls");
        if (options.NoXattrs) args.Add("--noxattrs");
        if (options.Sparse) args.Add("--sparse");
        return args;
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
