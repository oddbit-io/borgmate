using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using BorgMate.Models;
using BorgMate.Services.Keychain;
using Microsoft.Extensions.Logging;

namespace BorgMate.Services;

/// <summary>
/// Loads SSH keys into ssh-agent, piping passphrases via stdin.
/// </summary>
public class SshAgentHelper(ILogger<SshAgentHelper> logger, IKeychainService? keychain, WslHelper wsl)
{
    /// <summary>
    /// Ensures the SSH key is available for borg operations.
    /// On macOS/Linux: loads key into ssh-agent via expect or stdin.
    /// On WSL: prompts for passphrase and stores it on the repo for inline agent use.
    /// </summary>
    public async Task<bool> EnsureKeyLoadedAsync(string sshKeyPath, BorgRepository? repo = null)
    {
        if (string.IsNullOrWhiteSpace(sshKeyPath))
            return true;

        logger.LogDebug("EnsureKeyLoaded: key={Key}, WSL={IsWsl}", sshKeyPath, WslHelper.IsRequired);

        var keyPath = WslHelper.IsRequired
            ? await wsl.GetWslKeyPathAsync(sshKeyPath) ?? sshKeyPath
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
    /// Handles unencrypted keys: WSL needs no agent; macOS/Linux adds to agent silently.
    /// </summary>
    private async Task<bool> LoadUnencryptedKeyAsync(string sshKeyPath, string keyPath)
    {
        if (WslHelper.IsRequired) return true;

        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SSH_AUTH_SOCK")))
        {
            var (result, stderr) = await RunSshAddViaExpect(keyPath, null);
            logger.LogDebug("ssh-add (unencrypted) result={Result}, stderr={Stderr}", result, stderr);
        }
        return true;
    }

    /// <summary>
    /// WSL encrypted keys: prompts for passphrase and stores on repo for inline SSH_ASKPASS use.
    /// The passphrase must persist on the repo object because WSL has no ssh-agent —
    /// it's embedded in an SSH_ASKPASS script on every borg invocation.
    /// </summary>
    private async Task<bool> PromptWslPassphraseAsync(string sshKeyPath, BorgRepository? repo)
    {
        if (repo?.SshKeyPassphrase is not null)
            return true;

        var passphrase = await PromptSshKeyPassphraseAsync(sshKeyPath,
            string.Format(Strings.Get("EnterSshKeyPassphrase"), Path.GetFileName(sshKeyPath)));
        if (passphrase is null) return false;

        if (repo is not null)
            repo.SshKeyPassphrase = passphrase;
        return true;
    }

    /// <summary>
    /// macOS/Linux: loads encrypted key into persistent ssh-agent with up to 3 passphrase attempts.
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

            var (result, stderr) = await RunSshAddViaExpect(keyPath, passphrase);
            logger.LogDebug("ssh-add result={Result}, stderr={Stderr}", result, stderr);

            if (result == SshAddResult.Success)
            {
                logger.LogInformation("SSH key loaded into agent: {Key}", sshKeyPath);
                return true;
            }

            if (keychain is not null)
                await keychain.DeletePassphraseAsync($"sshkey:{sshKeyPath}");
        }

        logger.LogWarning("Failed to load SSH key after 3 attempts: {Key}", sshKeyPath);
        return true;
    }

    /// <summary>
    /// Gets the key fingerprint via ssh-keygen -lf (works on both public and private key files).
    /// </summary>
    private async Task<string?> GetKeyFingerprintAsync(string keyPath)
    {
        try
        {
            var (fileName, arguments) = WrapCommand("ssh-keygen", $"-lf {Quote(keyPath)}");
            var result = await RunProcessAsync(fileName, arguments);
            if (result.ExitCode != 0) return null;

            // Output: "256 SHA256:xxxxx comment (ED25519)"
            var parts = result.Stdout.Trim().Split(' ');
            return parts.Length >= 2 ? parts[1] : null;
        }
        catch (Exception ex) { logger.LogDebug(ex, "Failed to get SSH key fingerprint"); return null; }
    }

    /// <summary>
    /// Checks if a key with the given fingerprint is loaded in the agent.
    /// </summary>
    private async Task<bool> IsKeyInAgentAsync(string fingerprint)
    {
        try
        {
            var (fileName, arguments) = WrapCommand("ssh-add", "-l");
            var result = await RunProcessAsync(fileName, arguments);
            logger.LogDebug("ssh-add -l exit={ExitCode}, stdout={Stdout}", result.ExitCode, result.Stdout.TrimEnd());
            return result.ExitCode == 0 && result.Stdout.Contains(fingerprint);
        }
        catch (Exception ex) { logger.LogDebug(ex, "Failed to check SSH agent"); return false; }
    }

    /// <summary>
    /// Tests if a key is encrypted by trying to read it with an empty passphrase.
    /// ssh-keygen -y -P "" never opens /dev/tty — safe to call from GUI.
    /// </summary>
    private async Task<bool> IsKeyEncryptedAsync(string keyPath)
    {
        try
        {
            // Use -P '' with single quotes for reliable empty string across shells
            var passArg = WslHelper.IsRequired ? "-P ''" : "-P \"\"";
            var (fileName, arguments) = WrapCommand("ssh-keygen", $"-y {passArg} -f {Quote(keyPath)}");
            var result = await RunProcessAsync(fileName, arguments);
            logger.LogDebug("ssh-keygen encryption check: exit={ExitCode}, stderr={Stderr}",
                result.ExitCode, result.Stderr.TrimEnd());
            // Exit 0 = key is unencrypted (public key printed)
            if (result.ExitCode == 0) return false;
            // Command not found — can't determine, assume unencrypted to avoid false prompts
            if (result.ExitCode == 127 || result.Stderr.Contains("command not found", StringComparison.OrdinalIgnoreCase))
                return false;
            return true;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "ssh-keygen encryption check failed");
            return true; // Assume encrypted on error
        }
    }

    private enum SshAddResult { Success, NeedsPassphrase, OtherError }

    /// <summary>
    /// Adds a key to ssh-agent. On macOS, uses expect (Apple's ssh-add opens /dev/tty).
    /// On Linux/WSL, pipes passphrase via stdin (standard OpenSSH reads stdin when not tty).
    /// </summary>
    private static async Task<(SshAddResult result, string stderr)> RunSshAddViaExpect(
        string keyPath, string? passphrase)
    {
        if (passphrase is null)
        {
            // Unencrypted key — just run ssh-add directly with stdin closed
            var (fn, args) = WrapCommand("ssh-add", Quote(keyPath));
            var directResult = await RunProcessAsync(fn, args);
            return directResult.ExitCode == 0
                ? (SshAddResult.Success, directResult.Stderr)
                : (SshAddResult.OtherError, directResult.Stderr);
        }

        // Linux/WSL: standard OpenSSH reads passphrase from stdin when not a tty
        if (!OperatingSystem.IsMacOS())
            return await RunSshAddViaStdin(keyPath, passphrase);

        // macOS: Apple's ssh-add opens /dev/tty directly, need expect for pty
        return await RunSshAddViaExpectScript(keyPath, passphrase);
    }

    private static async Task<(SshAddResult result, string stderr)> RunSshAddViaStdin(
        string keyPath, string passphrase)
    {
        try
        {
            var (fileName, arguments) = WrapCommand("ssh-add", Quote(keyPath));
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi)!;
            await process.StandardInput.WriteLineAsync(passphrase);
            process.StandardInput.Close();
            var stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode == 0)
                return (SshAddResult.Success, stderr);

            var wrong = stderr.Contains("incorrect", StringComparison.OrdinalIgnoreCase)
                || stderr.Contains("bad", StringComparison.OrdinalIgnoreCase);
            return (wrong ? SshAddResult.NeedsPassphrase : SshAddResult.OtherError, stderr);
        }
        catch (Exception ex)
        {
            return (SshAddResult.OtherError, ex.Message);
        }
    }

    private static async Task<(SshAddResult result, string stderr)> RunSshAddViaExpectScript(
        string keyPath, string passphrase)
    {
        try
        {
            var script = BuildExpectScript(keyPath, passphrase);
            var expectResult = await RunProcessAsync("expect", "", stdinData: script);
            return ParseSshAddResult(expectResult);
        }
        catch (Exception ex)
        {
            return (SshAddResult.OtherError, ex.Message);
        }
    }

    /// <summary>
    /// Builds an expect script that spawns ssh-add and sends the passphrase.
    /// Escapes special Tcl/expect characters in both key path and passphrase.
    /// </summary>
    internal static string BuildExpectScript(string keyPath, string passphrase)
    {
        var escapedPass = EscapeForExpect(passphrase);
        var escapedKey = keyPath.Replace("\\", "\\\\").Replace("\"", "\\\"");

        return $$"""
               set timeout 10
               spawn ssh-add "{{escapedKey}}"
               expect {
                   -re "passphrase|password" { send "{{escapedPass}}\r"; exp_continue }
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

    /// <summary>
    /// Escapes a string for use inside an expect script double-quoted context.
    /// Must handle Tcl special characters: backslash, double-quote, brackets, dollar sign.
    /// </summary>
    internal static string EscapeForExpect(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"")
            .Replace("[", "\\[").Replace("]", "\\]").Replace("$", "\\$");

    private static (SshAddResult result, string stderr) ParseSshAddResult(ProcessOutput output)
    {
        if (output.ExitCode == 0)
            return (SshAddResult.Success, output.Stderr);

        if (output.ExitCode == 1
            || output.Stderr.Contains("incorrect", StringComparison.OrdinalIgnoreCase)
            || output.Stderr.Contains("bad", StringComparison.OrdinalIgnoreCase))
            return (SshAddResult.NeedsPassphrase, output.Stderr);

        return (SshAddResult.OtherError, output.Stderr);
    }

    private record ProcessOutput(int ExitCode, string Stdout, string Stderr);

    private static async Task<ProcessOutput> RunProcessAsync(string fileName, string arguments,
        string? stdinData = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

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
            var stored = await keychain.GetPassphraseAsync(keychainKey);
            if (!string.IsNullOrWhiteSpace(stored))
                return stored;
        }

        // Dialog must be created and shown on the UI thread
        var (result, saveToKeychain) = await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var window = DialogHelper.GetMainWindow();
            if (window is null) return ((string?)null, false);

            return await DialogHelper.ShowPasswordDialogAsync(window,
                Strings.Get("SshKeyPassphraseRequired"),
                message,
                Strings.Get("WatermarkSshKeyPassphrase"));
        });

        if (string.IsNullOrWhiteSpace(result))
            return null;

        if (saveToKeychain && keychain is not null)
            await keychain.SetPassphraseAsync(keychainKey, result);

        return result;
    }

    private static (string fileName, string arguments) WrapCommand(string command, string arguments)
    {
        if (WslHelper.IsRequired)
        {
            // Use bash -l (login shell) so PATH includes /usr/bin etc.
            var inner = $"{command} {arguments}".Replace("'", "'\\''");
            return ("wsl", $"-- bash -lc '{inner}'");
        }
        return (command, arguments);
    }

    private static string Quote(string value) => $"\"{value}\"";
}
