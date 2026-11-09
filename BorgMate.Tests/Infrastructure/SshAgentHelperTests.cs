using System.Diagnostics;
using BorgMate.Infrastructure.Platform;

namespace BorgMate.Tests.Infrastructure;

public class SshAgentHelperTests
{
    [Fact]
    public void BuildExpectScript_MovesEnvironmentValuesIntoVariablesBeforeSpawn()
    {
        var script = SshAgentHelper.BuildExpectScript();

        var unset = script.IndexOf("unset env(" + SshAgentHelper.ExpectKeyPathVariable + ") env("
                                   + SshAgentHelper.ExpectPassphraseVariable + ")", StringComparison.Ordinal);
        var spawn = script.IndexOf("spawn ssh-add -- $keyPath", StringComparison.Ordinal);
        Assert.InRange(unset, 0, spawn - 1);
        Assert.Contains("send -- \"$passphrase\\r\"", script);
    }

    [Fact]
    public void BuildExpectEnvironment_CarriesValuesVerbatim()
    {
        var environment = SshAgentHelper.BuildExpectEnvironment("/k/[exec x]\"", "-p$a[b]");

        Assert.Equal("/k/[exec x]\"", environment[SshAgentHelper.ExpectKeyPathVariable]);
        Assert.Equal("-p$a[b]", environment[SshAgentHelper.ExpectPassphraseVariable]);
    }

    [SkippableFact]
    public async Task BuildExpectScript_TclSubstitutionInKeyPath_IsNotEvaluatedAndEnvironmentIsClearedBeforeSpawn()
    {
        var tclsh = BashHarness.FindOnPath("tclsh");
        if (tclsh is null)
            throw new SkipException("tclsh is not on PATH.");

        using var temp = new TempDirectory();
        var directory = temp.Path;
        var keyPath = $"/k/[exec touch {directory}/tcl-marker] $HOME \"q\"";
        var passphrase = $"-p$a[exec touch {directory}/tcl-marker2]\\\"";
        var spawnLog = directory + "/spawn.txt";
        var sendLog = directory + "/send.txt";
        var spawnEnvironmentLog = directory + "/spawn-env.txt";
        var scriptFile = directory + "/run.tcl";
        var prelude = $$"""
                        proc spawn {args} {
                            set f [open {{{spawnLog}}} w]; puts $f [join $args \n]; close $f
                            set f [open {{{spawnEnvironmentLog}}} w]
                            puts $f "[info exists ::env({{SshAgentHelper.ExpectKeyPathVariable}})] [info exists ::env({{SshAgentHelper.ExpectPassphraseVariable}})]"
                            close $f
                        }
                        proc send {args} {
                            set f [open {{{sendLog}}} w]
                            fconfigure $f -translation binary
                            puts -nonewline $f [join $args \x1f]
                            close $f
                        }
                        proc exp_continue {} {}
                        proc expect {clauses} {
                            set i [lsearch -exact $clauses "passphrase|password"]
                            uplevel #0 [lindex $clauses [expr {$i + 1}]]
                        }

                        """;
        await File.WriteAllTextAsync(scriptFile, prelude + SshAgentHelper.BuildExpectScript());

        var psi = new ProcessStartInfo
        {
            FileName = tclsh,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        psi.ArgumentList.Add(scriptFile);
        foreach (var (key, value) in SshAgentHelper.BuildExpectEnvironment(keyPath, passphrase))
            psi.Environment[key] = value;
        using var process = Process.Start(psi)!;
        await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        Assert.True(process.ExitCode == 0, stderr);
        Assert.False(File.Exists(directory + "/tcl-marker"));
        Assert.False(File.Exists(directory + "/tcl-marker2"));
        Assert.Equal(["--", passphrase + "\r"], (await File.ReadAllTextAsync(sendLog)).Split('\x1f'));
        var spawned = (await File.ReadAllTextAsync(spawnLog)).Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.TrimEnd('\r'));
        Assert.Equal(["ssh-add", "--", keyPath], spawned);
        Assert.Equal("0 0", (await File.ReadAllTextAsync(spawnEnvironmentLog)).Trim());
    }

    [Fact]
    public void WrapCommand_Local_PassesArgumentsUntouched()
    {
        var invocation = SshAgentHelper.WrapCommand("ssh-keygen", ["-y", "-P", "", "-f", "/k/$(x)"], false);

        Assert.Equal("ssh-keygen", invocation.FileName);
        Assert.Equal(["-y", "-P", "", "-f", "/k/$(x)"], invocation.Arguments);
    }

    [SkippableFact]
    public async Task WrapCommand_Wsl_ShellMetacharactersArriveVerbatimAndAreNotExecuted()
    {
        BashHarness.RequireBash();

        using var temp = new TempDirectory();
        var directory = temp.Path;
        string[] values =
        [
            $"$(touch {directory}/marker)",
            $"`touch {directory}/marker2`",
            "a\"b",
            "",
            "-n"
        ];
        // The msys runtime on Windows re-parses single quotes in its command line; Linux runs the real value.
        if (!OperatingSystem.IsWindows())
            values = [.. values, "it's"];

        var invocation = SshAgentHelper.WrapCommand("printf", ["%s\n", .. values], true);
        Assert.Equal("wsl", invocation.FileName);
        var result = await BashHarness.RunWslArgumentsAsync(invocation.Arguments);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(values, result.Stdout.Split('\n').SkipLast(1));
        Assert.False(File.Exists(directory + "/marker"));
        Assert.False(File.Exists(directory + "/marker2"));
    }
}
