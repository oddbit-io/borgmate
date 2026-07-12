using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using BorgMate.Domain.Repositories;

namespace BorgMate.Infrastructure.Borg;

/// <summary>Detects local Borg executable paths for each supported Borg CLI generation.</summary>
public static class BorgPathHelper
{
    /// <summary>Auto-detects the Borg executable for the requested CLI generation.</summary>
    public static string Detect(BorgVersion borgVersion)
    {
        return DetectWithStatus(borgVersion).EffectivePath;
    }

    /// <summary>Auto-detects an executable by name without validating its Borg generation.</summary>
    public static string Detect(string binary = "borg")
    {
        if (OperatingSystem.IsWindows())
            return binary;

        foreach (var candidate in ResolveCandidates(binary))
            if (TryReadCliVersion(candidate) is not null)
                return candidate;

        return binary;
    }

    internal static BorgPathDetectionResult DetectWithStatus(BorgVersion borgVersion)
    {
        return DetectWithStatus(borgVersion, ResolveCandidates, TryReadCliVersion);
    }

    internal static BorgPathDetectionResult DetectWithStatus(
        BorgVersion borgVersion,
        Func<string, IEnumerable<string>> candidatesForBinary,
        Func<string, BorgCliVersion?> versionProbe)
    {
        var expectedMajor = borgVersion == BorgVersion.Borg2 ? 2 : 1;
        var borgCommandWasBorg2 = false;

        foreach (var binary in GetBinaryPreference(borgVersion))
        {
            foreach (var candidate in candidatesForBinary(binary))
            {
                var version = versionProbe(candidate);
                if (version is null)
                    continue;

                if (binary == "borg" && version.Major == 2)
                    borgCommandWasBorg2 = true;

                if (version.Major == expectedMajor)
                    return new BorgPathDetectionResult(candidate, candidate);
            }
        }

        var fallback = borgVersion == BorgVersion.Borg1 && borgCommandWasBorg2
            ? "borg1"
            : DefaultFallbackCommand(borgVersion);

        return new BorgPathDetectionResult(fallback, null);
    }

    private static string[] GetBinaryPreference(BorgVersion borgVersion) =>
        borgVersion == BorgVersion.Borg2 ? ["borg2", "borg"] : ["borg", "borg1"];

    private static string DefaultFallbackCommand(BorgVersion borgVersion) =>
        borgVersion == BorgVersion.Borg2 ? "borg2" : "borg";

    private static IReadOnlyList<string> ResolveCandidates(string binary)
    {
        var candidates = new List<string>();

        var platformCandidates = OperatingSystem.IsMacOS() ? BuildMacOsPaths(binary)
            : OperatingSystem.IsLinux() ? BuildLinuxPaths(binary)
            : Array.Empty<string>();

        foreach (var path in platformCandidates)
            if (File.Exists(path))
                AddCandidate(candidates, path);

        // Ask PATH as well; this catches Nix, custom installs, and normal distro packages.
        if (!OperatingSystem.IsWindows())
        {
            var found = RunWhich(binary);
            if (found is not null)
                AddCandidate(candidates, found);
        }

        AddCandidate(candidates, binary);
        return candidates;
    }

    private static string[] BuildMacOsPaths(string binary) =>
    [
        $"/opt/homebrew/bin/{binary}",
        $"/usr/local/bin/{binary}",
        $"/opt/local/bin/{binary}"
    ];

    private static string[] BuildLinuxPaths(string binary) =>
    [
        $"/usr/bin/{binary}",
        $"/usr/local/bin/{binary}",
        $"/snap/bin/{binary}",
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), $".local/bin/{binary}"),
        $"/home/linuxbrew/.linuxbrew/bin/{binary}"
    ];

    private static void AddCandidate(List<string> candidates, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        if (!candidates.Exists(candidate => string.Equals(candidate, path, StringComparison.Ordinal)))
            candidates.Add(path);
    }

    private static BorgCliVersion? TryReadCliVersion(string candidate)
    {
        var output = OperatingSystem.IsWindows()
            ? RunWslVersion(candidate)
            : RunVersion(candidate);

        return output is null ? null : BorgCliVersion.Parse(output);
    }

    private static string? RunVersion(string candidate)
    {
        var psi = new ProcessStartInfo(candidate)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("--version");
        return RunProcess(psi);
    }

    private static string? RunWslVersion(string candidate)
    {
        var psi = new ProcessStartInfo("wsl")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("--");
        psi.ArgumentList.Add("bash");
        psi.ArgumentList.Add("-lc");
        psi.ArgumentList.Add($"{ShellQuote(candidate)} --version");
        return RunProcess(psi);
    }

    private static string? RunWhich(string binary)
    {
        var psi = new ProcessStartInfo("which")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add(binary);
        return RunProcess(psi);
    }

    private static string? RunProcess(ProcessStartInfo psi)
    {
        psi.Environment["LC_ALL"] = "C";
        try
        {
            using var proc = new Process { StartInfo = psi };
            var stdout = new StringBuilder();
            var stderr = new StringBuilder();
            proc.OutputDataReceived += (_, e) => AppendLine(stdout, e.Data);
            proc.ErrorDataReceived += (_, e) => AppendLine(stderr, e.Data);

            if (!proc.Start()) return null;
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            if (!proc.WaitForExit(3000))
            {
                TryKill(proc);
                proc.WaitForExit(1000);
                return null;
            }

            proc.WaitForExit();
            var output = stdout.ToString().Trim();
            var error = stderr.ToString().Trim();
            if (proc.ExitCode != 0)
                return null;

            return output.Length > 0 ? output : error.Length > 0 ? error : null;
        }
        catch (Win32Exception)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static void AppendLine(StringBuilder builder, string? value)
    {
        if (value is not null)
            builder.AppendLine(value);
    }

    private static void TryKill(Process process)
    {
        try
        {
            process.Kill(true);
        }
        catch (InvalidOperationException)
        {
            // The process exited between timeout detection and kill.
        }
    }

    private static string ShellQuote(string value)
    {
        return "'" + value.Replace("'", "'\\''") + "'";
    }
}
