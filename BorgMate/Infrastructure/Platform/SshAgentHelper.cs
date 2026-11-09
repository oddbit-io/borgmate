using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using BorgMate.Domain.Repositories;
using BorgMate.Infrastructure.Notifications;
using BorgMate.Persistence.Keychain;
using BorgMate.Presentation.Dialogs;
using BorgMate.Runtime.Orchestration;
using BorgMate.Runtime.Security;
using Microsoft.Extensions.Logging;
using OneOf;

namespace BorgMate.Infrastructure.Platform;

/// <summary>
///     Loads SSH keys into ssh-agent, piping passphrases via stdin.
/// </summary>
public class SshAgentHelper(
    ILogger<SshAgentHelper> logger, ICredentialStore? keychain, WslHelper wsl,
    INotificationService? notifier = null)
{
    internal const string ExpectKeyPathVariable = "BORGMATE_SSH_KEY_PATH";
    internal const string ExpectPassphraseVariable = "BORGMATE_SSH_PASSPHRASE";
    private const string WslExecScript = "exec \"$@\"";

    private readonly ConcurrentDictionary<string, string> _wslKeyPassphrases = new(StringComparer.Ordinal);

    public virtual string? GetWslKeyPassphrase(string sshKeyPath)
    {
        return _wslKeyPassphrases.TryGetValue(sshKeyPath, out var passphrase) ? passphrase : null;
    }

    /// <summary>
    ///     Ensures the SSH key is available for borg operations.
    ///     On macOS/Linux: loads key into ssh-agent via expect or stdin.
    ///     On WSL: prompts for passphrase and stores it on the repo for inline agent use.
    /// </summary>
    public virtual async Task<bool> EnsureKeyLoadedAsync(
        string sshKeyPath,
        BorgRepository? repo = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sshKeyPath))
            return true;

        logger.LogDebug("EnsureKeyLoaded: key={Key}, WSL={IsWsl}", sshKeyPath, WslHelper.IsRequired);

        if (WslHelper.IsRequired && !await wsl.IsAvailableAsync(ct))
            return true;

        var keyPath = WslHelper.IsRequired
            ? await wsl.GetWslKeyPathAsync(sshKeyPath, ct) ?? sshKeyPath
            : sshKeyPath;

        var encrypted = await IsKeyEncryptedAsync(keyPath);
        logger.LogDebug("Key {Key} encrypted: {Encrypted}", sshKeyPath, encrypted);

        if (!encrypted)
            return await LoadUnencryptedKeyAsync(sshKeyPath, keyPath);

        if (WslHelper.IsRequired)
            return await PromptWslPassphraseAsync(sshKeyPath, repo);

        return await LoadEncryptedKeyIntoAgentAsync(sshKeyPath, keyPath);
    }

    /// <summary>
    ///     Handles unencrypted keys: WSL needs no agent; macOS/Linux adds to agent silently.
    /// </summary>
    private async Task<bool> LoadUnencryptedKeyAsync(string sshKeyPath, string keyPath)
    {
        if (WslHelper.IsRequired) return true;

        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SSH_AUTH_SOCK")))
        {
            var result = await RunSshAddViaExpect(keyPath, null);
            logger.LogDebug("ssh-add (unencrypted) result={Result}", result.Value);
        }

        return true;
    }

    /// <summary>
    ///     WSL encrypted keys: prompts for passphrase and stores on repo for inline SSH_ASKPASS use.
    ///     The passphrase must persist on the repo object because WSL has no ssh-agent —
    ///     it reaches an SSH_ASKPASS script through the environment on every borg invocation.
    /// </summary>
    private async Task<bool> PromptWslPassphraseAsync(string sshKeyPath, BorgRepository? repo)
    {
        if (_wslKeyPassphrases.ContainsKey(sshKeyPath))
            return true;

        var passphrase = await PromptSshKeyPassphraseAsync(sshKeyPath,
            string.Format(Strings.Get("EnterSshKeyPassphrase"), Path.GetFileName(sshKeyPath)));
        if (passphrase is null) return false;

        _wslKeyPassphrases[sshKeyPath] = passphrase;
        return true;
    }

    /// <summary>
    ///     macOS/Linux: loads encrypted key into persistent ssh-agent with up to 3 passphrase attempts.
    /// </summary>
    private async Task<bool> LoadEncryptedKeyIntoAgentAsync(string sshKeyPath, string keyPath)
    {
        var authSock = Environment.GetEnvironmentVariable("SSH_AUTH_SOCK");
        if (string.IsNullOrEmpty(authSock))
        {
            logger.LogWarning("SSH_AUTH_SOCK not set, skipping agent load");
            return true;
        }

        var fingerprint = await GetKeyFingerprintAsync(keyPath);
        if (fingerprint is not null && await IsKeyInAgentAsync(fingerprint))
        {
            logger.LogDebug("SSH key already in agent: {Key}", sshKeyPath);
            return true;
        }

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var message = attempt == 0
                ? string.Format(Strings.Get("EnterSshKeyPassphrase"), Path.GetFileName(sshKeyPath))
                : Strings.Get("WrongSshKeyPassphrase");

            var passphrase = await PromptSshKeyPassphraseAsync(sshKeyPath, message);
            if (passphrase is null) return false;

            var result = await RunSshAddViaExpect(keyPath, passphrase);
            logger.LogDebug("ssh-add result={Result}", result.Value);

            if (result.IsT0)
            {
                logger.LogInformation("SSH key loaded into agent: {Key}", sshKeyPath);
                return true;
            }

            if (keychain is not null && OperationInteractivity.IsInteractive)
                await keychain.DeletePassphraseAsync($"sshkey:{sshKeyPath}");
        }

        logger.LogWarning("Failed to load SSH key after 3 attempts: {Key}", sshKeyPath);
        return true;
    }

    /// <summary>
    ///     Gets the key fingerprint via ssh-keygen -lf (works on both public and private key files).
    /// </summary>
    private async Task<string?> GetKeyFingerprintAsync(string keyPath)
    {
        try
        {
            var invocation = WrapCommand("ssh-keygen", ["-lf", keyPath], WslHelper.IsRequired);
            var result = await RunProcessAsync(invocation);
            if (result.ExitCode != 0) return null;

            // Output: "256 SHA256:xxxxx comment (ED25519)"
            var parts = result.Stdout.Trim().Split(' ');
            return parts.Length >= 2 ? parts[1] : null;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to get SSH key fingerprint");
            return null;
        }
    }

    /// <summary>
    ///     Checks if a key with the given fingerprint is loaded in the agent.
    /// </summary>
    private async Task<bool> IsKeyInAgentAsync(string fingerprint)
    {
        try
        {
            var invocation = WrapCommand("ssh-add", ["-l"], WslHelper.IsRequired);
            var result = await RunProcessAsync(invocation);
            logger.LogDebug("ssh-add -l exit={ExitCode}, stdout={Stdout}", result.ExitCode, result.Stdout.TrimEnd());
            return result.ExitCode == 0 && result.Stdout.Contains(fingerprint);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to check SSH agent");
            return false;
        }
    }

    /// <summary>
    ///     Tests if a key is encrypted by trying to read it with an empty passphrase.
    ///     ssh-keygen -y -P "" never opens /dev/tty — safe to call from GUI.
    /// </summary>
    private async Task<bool> IsKeyEncryptedAsync(string keyPath)
    {
        try
        {
            var invocation = WrapCommand("ssh-keygen", ["-y", "-P", "", "-f", keyPath], WslHelper.IsRequired);
            var result = await RunProcessAsync(invocation);
            logger.LogDebug("ssh-keygen encryption check: exit={ExitCode}, stderr={Stderr}",
                result.ExitCode, result.Stderr.TrimEnd());
            // Exit 0 = key is unencrypted (public key printed)
            if (result.ExitCode == 0) return false;
            // Command not found — can't determine, assume unencrypted to avoid false prompts
            if (result.ExitCode == 127 ||
                result.Stderr.Contains("command not found", StringComparison.OrdinalIgnoreCase))
                return false;
            return true;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "ssh-keygen encryption check failed");
            return true; // Assume encrypted on error
        }
    }

    /// <summary>
    ///     Adds a key to ssh-agent. On macOS, uses expect (Apple's ssh-add opens /dev/tty).
    ///     On Linux/WSL, pipes passphrase via stdin (standard OpenSSH reads stdin when not tty).
    /// </summary>
    private static async Task<OneOf<SshKeyAdded, SshAddError>> RunSshAddViaExpect(
        string keyPath, string? passphrase)
    {
        if (passphrase is null)
        {
            // Unencrypted key — just run ssh-add directly with stdin closed
            var directResult = await RunProcessAsync(BuildSshAddInvocation(keyPath));
            if (directResult.ExitCode == 0)
                return new SshKeyAdded(directResult.Stderr);

            return new SshAddError.Failed(directResult.Stderr);
        }

        // Linux/WSL: standard OpenSSH reads passphrase from stdin when not a tty
        if (!OperatingSystem.IsMacOS())
            return await RunSshAddViaStdin(keyPath, passphrase);

        // macOS: Apple's ssh-add opens /dev/tty directly, need expect for pty
        return await RunSshAddViaExpectScript(keyPath, passphrase);
    }

    private static async Task<OneOf<SshKeyAdded, SshAddError>> RunSshAddViaStdin(
        string keyPath, string passphrase)
    {
        try
        {
            var psi = CreateStartInfo(BuildSshAddInvocation(keyPath));

            using var process = Process.Start(psi)!;
            await process.StandardInput.WriteLineAsync(passphrase);
            process.StandardInput.Close();
            var stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode == 0)
                return new SshKeyAdded(stderr);

            var wrong = stderr.Contains("incorrect", StringComparison.OrdinalIgnoreCase)
                        || stderr.Contains("bad", StringComparison.OrdinalIgnoreCase);
            if (wrong)
                return new SshAddError.WrongPassphrase(stderr);

            return new SshAddError.Failed(stderr);
        }
        catch (Exception ex)
        {
            return new SshAddError.Failed(ex.Message);
        }
    }

    private static async Task<OneOf<SshKeyAdded, SshAddError>> RunSshAddViaExpectScript(
        string keyPath, string passphrase)
    {
        try
        {
            var expectResult = await RunProcessAsync(
                new ProcessInvocation { FileName = "expect", Arguments = [] }, BuildExpectScript(),
                BuildExpectEnvironment(keyPath, passphrase));
            return ParseSshAddResult(expectResult);
        }
        catch (Exception ex)
        {
            return new SshAddError.Failed(ex.Message);
        }
    }

    internal static IReadOnlyDictionary<string, string> BuildExpectEnvironment(string keyPath, string passphrase)
    {
        return new Dictionary<string, string>
        {
            [ExpectKeyPathVariable] = keyPath,
            [ExpectPassphraseVariable] = passphrase
        };
    }

    /// <summary>
    ///     Builds the constant expect script that spawns ssh-add and sends the passphrase. The key path and
    ///     passphrase arrive through the <see cref="ExpectKeyPathVariable" /> and <see cref="ExpectPassphraseVariable" />
    ///     environment variables; Tcl does not re-parse substituted values, so no escaping is needed. The script
    ///     moves both values into Tcl variables and unsets the environment entries before spawning, so ssh-add
    ///     does not inherit the passphrase.
    /// </summary>
    internal static string BuildExpectScript()
    {
        return $$"""
                 set timeout 10
                 set keyPath $env({{ExpectKeyPathVariable}})
                 set passphrase $env({{ExpectPassphraseVariable}})
                 unset env({{ExpectKeyPathVariable}}) env({{ExpectPassphraseVariable}})
                 spawn ssh-add -- $keyPath
                 expect {
                     -re "passphrase|password" { send -- "$passphrase\r"; exp_continue }
                     "Identity added" { exit 0 }
                     -re "Bad|incorrect" { exit 1 }
                     timeout { exit 2 }
                     eof {
                         catch wait result
                         exit [lindex $result 3]
                     }
                 }
                 """;
    }

    private static OneOf<SshKeyAdded, SshAddError> ParseSshAddResult(ProcessOutput output)
    {
        if (output.ExitCode == 0)
            return new SshKeyAdded(output.Stderr);

        if (output.ExitCode == 1
            || output.Stderr.Contains("incorrect", StringComparison.OrdinalIgnoreCase)
            || output.Stderr.Contains("bad", StringComparison.OrdinalIgnoreCase))
            return new SshAddError.WrongPassphrase(output.Stderr);

        return new SshAddError.Failed(output.Stderr);
    }

    private static ProcessStartInfo CreateStartInfo(ProcessInvocation invocation)
    {
        var psi = new ProcessStartInfo
        {
            FileName = invocation.FileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in invocation.Arguments)
            psi.ArgumentList.Add(argument);
        psi.Environment["LC_ALL"] = "C";
        return psi;
    }

    private static async Task<ProcessOutput> RunProcessAsync(ProcessInvocation invocation,
        string? stdinData = null, IReadOnlyDictionary<string, string>? environment = null)
    {
        var psi = CreateStartInfo(invocation);
        if (environment is not null)
            foreach (var (key, value) in environment)
                psi.Environment[key] = value;

        using var process = Process.Start(psi)!;
        if (stdinData is not null)
            await process.StandardInput.WriteAsync(stdinData);
        process.StandardInput.Close();
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new ProcessOutput(process.ExitCode, stdout, stderr);
    }

    private async Task<string?> PromptSshKeyPassphraseAsync(string sshKeyPath, string message)
    {
        var keychainKey = $"sshkey:{sshKeyPath}";

        if (keychain is not null)
        {
            var stored = await keychain.GetPassphraseAsync(keychainKey, OperationInteractivity.IsInteractive);
            if (!string.IsNullOrWhiteSpace(stored))
                return stored;
        }

        // Scheduled runs must fail loudly here: EnsureKeyLoadedAsync's return value is
        // ignored by BuildEnvironmentAsync, so returning null would surface as an opaque
        // SSH failure from borg instead of this message.
        if (!OperationInteractivity.IsInteractive)
            throw new CredentialsRequiredException(
                string.Format(Strings.Get("Error.SshKeyPassphraseRequired.Unattended"),
                    Path.GetFileName(sshKeyPath)));

        // Dialog must be created and shown on the UI thread
        var (result, saveToKeychain) = await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var window = DialogHelper.GetDialogOwner();
            if (window is null) return ((string?)null, false);

            return await DialogHelper.ShowPasswordDialogAsync(window,
                Strings.Get("SshKeyPassphraseRequired"),
                message,
                Strings.Get("WatermarkSshKeyPassphrase"));
        });

        if (string.IsNullOrWhiteSpace(result))
            return null;

        if (saveToKeychain && keychain is not null)
            await keychain.TrySetPassphraseAsync(notifier, keychainKey, result);

        return result;
    }

    private static ProcessInvocation BuildSshAddInvocation(string keyPath)
    {
        return WrapCommand("ssh-add", ["--", keyPath], WslHelper.IsRequired);
    }

    /// <summary>
    ///     Under WSL the command runs through a login bash (PATH includes /usr/bin etc.) whose constant script
    ///     execs the command with the remaining argv elements.
    /// </summary>
    internal static ProcessInvocation WrapCommand(string command, IReadOnlyList<string> arguments, bool viaWsl)
    {
        if (!viaWsl)
            return new ProcessInvocation { FileName = command, Arguments = arguments };

        List<string> wslArguments = ["--exec", "bash", "-lc", WslExecScript, "bash", command];
        wslArguments.AddRange(arguments);
        return new ProcessInvocation { FileName = "wsl", Arguments = wslArguments };
    }

    private record ProcessOutput(int ExitCode, string Stdout, string Stderr);
}
