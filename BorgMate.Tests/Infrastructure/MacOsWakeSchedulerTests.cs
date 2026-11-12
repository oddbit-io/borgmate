using BorgMate.Infrastructure.Platform;

namespace BorgMate.Tests.Infrastructure;

public class MacOsWakeSchedulerTests
{
    [Fact]
    public void BuildInstallCommand_InstallsOnlyFilesRootWroteItself()
    {
        var command = MacOsWakeScheduler.BuildInstallCommand("alice", ["#!/bin/sh"]);

        Assert.DoesNotContain(Path.GetTempPath().TrimEnd('/', '\\'), command);
        Assert.Contains("t=$(/usr/bin/mktemp -d /tmp/borgmate.XXXXXX)", command);
        Assert.Contains("/usr/sbin/visudo -c -q -f \"$t/sudoers\"", command);
        Assert.Contains("/usr/bin/install -o root -g wheel -m 0440 \"$t/sudoers\" /etc/sudoers.d/borgmate", command);
        Assert.Contains("'alice ALL=(root) NOPASSWD: /Library/PrivilegedHelperTools/borgmate-pmset'", command);
        Assert.DoesNotContain("/usr/bin/pmset", command);
    }

    [SkippableFact]
    public async Task WriteLinesCommand_ReproducesHelperScriptExactly()
    {
        BashHarness.RequireBash();
        using var directory = new TempDirectory();
        var script = MacOsWakeScheduler.ReadHelperScript();

        var command = MacOsWakeScheduler.WriteLinesCommand(script, "$t/helper");
        var result = await BashHarness.RunWslArgumentsAsync(
            ["--exec", "bash", "-c", $"t='{directory.Path}' && {command}"]);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(string.Join('\n', script) + "\n", await File.ReadAllTextAsync($"{directory.Path}/helper"));
    }

    [SkippableTheory]
    [InlineData("schedule", "11/12/26 03:00:00; touch pwned")]
    [InlineData("schedule", "$(touch pwned)")]
    [InlineData("rm", "11/12/26 03:00:00")]
    public async Task HelperScript_RejectsAnythingButBorgMateWakeEvents(string action, string date)
    {
        BashHarness.RequireBash();
        using var directory = new TempDirectory();
        await File.WriteAllTextAsync($"{directory.Path}/helper",
            string.Join('\n', MacOsWakeScheduler.ReadHelperScript()) + "\n");

        var result = await BashHarness.RunWslArgumentsAsync(
            ["--exec", "bash", "-c", "cd \"$1\" && sh ./helper \"$2\" \"$3\"", "bash", directory.Path, action, date]);

        Assert.Equal(64, result.ExitCode);
        Assert.False(File.Exists($"{directory.Path}/pwned"));
    }
}
