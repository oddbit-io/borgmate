using BorgMate.Infrastructure.Borg;
using BorgMate.Infrastructure.Platform;
using BorgMate.Runtime.Orchestration;
using BorgMate.Runtime.Security;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace BorgMate.Tests.Infrastructure;

public class WslSshHostKeyTrustTests
{
    private const string HostKey = "[backup.example.com]:2222 ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIExample";

    [Fact]
    public async Task EnsureTrusted_KnownHost_AsksNothing()
    {
        var trust = new ScriptedHostKeyTrust { KnownHost = true };

        await trust.EnsureTrustedAsync("borg@backup.example.com", 2222, CancellationToken.None);

        Assert.Equal(0, trust.Prompts);
        Assert.DoesNotContain(trust.Scripts, script => script.Contains("known_hosts && chmod"));
        Assert.Equal(["[backup.example.com]:2222"], trust.Parameters[0]);
    }

    [Fact]
    public async Task EnsureTrusted_UnknownHostConfirmed_AddsScannedKeys()
    {
        var trust = new ScriptedHostKeyTrust { Confirmed = true };

        await trust.EnsureTrustedAsync("borg@backup.example.com", 2222, CancellationToken.None);

        Assert.Equal(1, trust.Prompts);
        Assert.NotNull(trust.AppendedKeys);
        Assert.Equal([HostKey], trust.AppendedKeys);
    }

    [Fact]
    public async Task EnsureTrusted_UnknownHostRejected_AddsNothing()
    {
        var trust = new ScriptedHostKeyTrust { Confirmed = false };

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            trust.EnsureTrustedAsync("borg@backup.example.com", 2222, CancellationToken.None));

        Assert.Null(trust.AppendedKeys);
    }

    [Fact]
    public async Task EnsureTrusted_UnknownHostInScheduledRun_FailsWithoutScanning()
    {
        var trust = new ScriptedHostKeyTrust { Confirmed = true };

        await Task.Run(async () =>
        {
            OperationInteractivity.IsInteractive = false;
            await Assert.ThrowsAsync<CredentialsRequiredException>(() =>
                trust.EnsureTrustedAsync("backup.example.com", 22, CancellationToken.None));
        });

        Assert.Single(trust.Scripts);
        Assert.Equal(0, trust.Prompts);
    }

    [Theory]
    [InlineData("borg@backup.example.com", 22, "backup.example.com")]
    [InlineData("backup.example.com", 2222, "[backup.example.com]:2222")]
    [InlineData("borg@[2001:db8::1]", 2222, "[2001:db8::1]:2222")]
    public void KnownHostsName_MatchesOpenSshHostFormat(string destination, int port, string expected)
    {
        Assert.Equal(expected,
            WslSshHostKeyTrust.KnownHostsName(WslSshHostKeyTrust.HostName(destination), port));
    }

    private class ScriptedHostKeyTrust : WslSshHostKeyTrust
    {
        public ScriptedHostKeyTrust()
            : base(Substitute.For<ILogger<WslSshHostKeyTrust>>())
        {
        }

        public bool KnownHost { get; init; }
        public bool Confirmed { get; init; }
        public int Prompts { get; private set; }
        public List<string> Scripts { get; } = [];
        public List<string[]> Parameters { get; } = [];
        public string[]? AppendedKeys { get; private set; }

        protected override Task<BorgResult> RunAsync(string script, IEnumerable<string> parameters,
            CancellationToken ct)
        {
            var values = parameters.ToArray();
            Scripts.Add(script);
            Parameters.Add(values);
            if (script.StartsWith("ssh-keygen -F", StringComparison.Ordinal))
                return Result(KnownHost ? 0 : 1, "");
            if (script.StartsWith("ssh-keyscan", StringComparison.Ordinal))
                return Result(0, $"# backup.example.com:2222 SSH-2.0-OpenSSH_9.6\n{HostKey}\n");
            if (script.Contains("ssh-keygen -l", StringComparison.Ordinal))
                return Result(0, "256 SHA256:abcdef [backup.example.com]:2222 (ED25519)\n");

            AppendedKeys = values;
            return Result(0, "");
        }

        protected override Task<bool> ConfirmAsync(string host, int port, IReadOnlyList<string> fingerprints,
            CancellationToken ct)
        {
            Prompts++;
            return Task.FromResult(Confirmed);
        }

        private static Task<BorgResult> Result(int exitCode, string stdout)
        {
            return Task.FromResult(new BorgResult(exitCode, stdout, ""));
        }
    }
}
