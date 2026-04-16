using BorgMate.Models;
using BorgMate.Services;
using BorgMate.Services.Borg;
using BorgMate.Services.Config;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace BorgMate.Tests;

/// <summary>
/// Tests Borg2Service command argument construction.
/// Verifies borg2-specific syntax differences (rcreate, rlist, rinfo, --repo flag).
/// </summary>
public class Borg2ServiceTests
{
    private static readonly WslHelper Wsl = new(Substitute.For<ILogger<WslHelper>>());
    private static readonly SshAgentHelper SshAgent = new(Substitute.For<ILogger<SshAgentHelper>>(), null, Wsl);

    private static TestBorg2Service CreateService(AppSettings? settings = null) =>
        new(Substitute.For<ILogger<Borg2Service>>(), settings ?? new AppSettings(), SshAgent, Wsl);

    private static BorgRepository LocalRepo(string path = "/repo") =>
        new() { Name = "test", Path = path, IsLocal = true, EncryptionMode = BorgEncryptionMode.None };

    [Fact]
    public async Task InitAsync_UsesRcreate()
    {
        var svc = CreateService();
        var repo = LocalRepo();
        repo.EncryptionMode = BorgEncryptionMode.RepokeyBlake2;

        await svc.InitAsync(repo);

        Assert.Contains("rcreate", svc.LastArgs!);
        Assert.Contains("--repo", svc.LastArgs!);
        Assert.Contains("--encryption repokey-blake2", svc.LastArgs!);
    }

    [Fact]
    public async Task ListArchivesAsync_UsesRlist()
    {
        var svc = CreateService();
        var repo = LocalRepo();

        await svc.ListArchivesAsync(repo);

        Assert.Contains("rlist", svc.LastArgs!);
        Assert.Contains("--json", svc.LastArgs!);
    }

    [Fact]
    public async Task DeleteArchiveAsync_UsesSeparateRepoFlag()
    {
        var svc = CreateService();
        var repo = LocalRepo();

        await svc.DeleteArchiveAsync(repo, "old-archive");

        Assert.Contains("delete", svc.LastArgs!);
        Assert.Contains("--repo", svc.LastArgs!);
        // Borg2 separates repo and archive name
        Assert.Contains("old-archive", svc.LastArgs!);
    }

    [Fact]
    public async Task PruneAsync_UsesRepoFlag()
    {
        var svc = CreateService();
        var repo = LocalRepo();

        await svc.PruneAsync(repo);

        Assert.Contains("prune", svc.LastArgs!);
        Assert.Contains("--repo", svc.LastArgs!);
        Assert.Contains("--stats", svc.LastArgs!);
    }

    [Fact]
    public async Task PruneAsync_EmitsKeepFlagsFromPruneOptions()
    {
        var svc = CreateService();
        var repo = LocalRepo();
        repo.PruneOptions.KeepLast = 10;
        repo.PruneOptions.KeepYearly = 2;

        await svc.PruneAsync(repo);

        Assert.Contains("--keep-last 10", svc.LastArgs!);
        Assert.Contains("--keep-yearly 2", svc.LastArgs!);
    }

    [Fact]
    public async Task CheckAsync_UsesRepoFlag()
    {
        var svc = CreateService();
        var repo = LocalRepo();

        await svc.CheckAsync(repo);

        Assert.Contains("check", svc.LastArgs!);
        Assert.Contains("--repo", svc.LastArgs!);
        Assert.Contains("--progress", svc.LastArgs!);
    }

    [Fact]
    public async Task CompactAsync_UsesRepoFlag()
    {
        var svc = CreateService();
        var repo = LocalRepo();

        await svc.CompactAsync(repo);

        Assert.Contains("compact", svc.LastArgs!);
        Assert.Contains("--repo", svc.LastArgs!);
        Assert.Contains("--progress", svc.LastArgs!);
    }

    [Fact]
    public async Task CreateBackupAsync_RateLimitUsesUploadRatelimit()
    {
        var svc = CreateService();
        var repo = LocalRepo();
        repo.RateLimit = 500;

        await svc.CreateBackupAsync(repo, "archive", ["/data"]);

        Assert.Contains("--upload-ratelimit 500", svc.LastArgs!);
        Assert.DoesNotContain("--remote-ratelimit", svc.LastArgs!);
    }

    [Fact]
    public async Task InfoRepoAsync_UsesRinfo()
    {
        var svc = CreateService();
        var repo = LocalRepo();

        await svc.InfoRepoAsync(repo);

        Assert.Contains("rinfo", svc.LastArgs!);
        Assert.Contains("--json", svc.LastArgs!);
    }

    [Fact]
    public async Task DiffArchivesAsync_SameSyntaxAsBorg1()
    {
        var svc = CreateService();
        var repo = LocalRepo();

        await svc.DiffArchivesAsync(repo, "a1", "a2");

        Assert.Contains("diff", svc.LastArgs!);
        Assert.Contains($"{repo.Path}::a1", svc.LastArgs!);
        Assert.Contains("a2", svc.LastArgs!);
    }

    [Fact]
    public async Task ListArchiveContentsAsync_ProducesJsonLines()
    {
        var svc = CreateService();
        var repo = LocalRepo();

        await svc.ListArchiveContentsAsync(repo, "archive");

        Assert.Contains("list", svc.LastArgs!);
        Assert.Contains("--json-lines", svc.LastArgs!);
    }

    /// <summary>
    /// Test subclass that captures RunCommandAsync arguments instead of executing processes.
    /// </summary>
    private class TestBorg2Service(ILogger<Borg2Service> logger, AppSettings settings, SshAgentHelper sshAgent, WslHelper wsl)
        : Borg2Service(logger, settings, sshAgent, wsl)
    {
        public string? LastFileName { get; private set; }
        public string? LastArgs { get; private set; }

        protected override Task<BorgResult> RunCommandAsync(
            string fileName, string arguments, BorgEnv? env = null,
            string? workingDirectory = null, CancellationToken ct = default,
            Action<string>? onStderrLine = null)
        {
            LastFileName = fileName;
            LastArgs = arguments;
            return Task.FromResult(new BorgResult(0, "", ""));
        }
    }
}
