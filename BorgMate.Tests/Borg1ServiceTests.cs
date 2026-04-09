using BorgMate.Models;
using BorgMate.Services;
using BorgMate.Services.Borg;
using BorgMate.Services.Config;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace BorgMate.Tests;

/// <summary>
/// Tests Borg1Service command argument construction.
/// Uses a test subclass that captures arguments passed to RunCommandAsync.
/// </summary>
public class Borg1ServiceTests
{
    private static readonly WslHelper Wsl = new(Substitute.For<ILogger<WslHelper>>());
    private static readonly SshAgentHelper SshAgent = new(Substitute.For<ILogger<SshAgentHelper>>(), null, Wsl);

    private static TestBorg1Service CreateService(AppSettings? settings = null) =>
        new(Substitute.For<ILogger<Borg1Service>>(), settings ?? new AppSettings(), SshAgent, Wsl);

    private static BorgRepository LocalRepo(string path = "/repo", BorgEncryptionMode enc = BorgEncryptionMode.None) =>
        new() { Name = "test", Path = path, IsLocal = true, EncryptionMode = enc };

    [Fact]
    public async Task InitAsync_LocalRepo_ProducesCorrectArgs()
    {
        var svc = CreateService();
        var repo = LocalRepo();
        repo.EncryptionMode = BorgEncryptionMode.RepokeyBlake2;

        await svc.InitAsync(repo);

        Assert.Contains("init", svc.LastArgs!);
        Assert.Contains("--encryption repokey-blake2", svc.LastArgs!);
        Assert.Contains(repo.Path, svc.LastArgs!);
    }

    [Fact]
    public async Task CreateBackupAsync_ProducesCorrectArgs()
    {
        var svc = CreateService();
        var repo = LocalRepo();
        var sources = new[] { "/home/user/docs", "/home/user/pics" };

        await svc.CreateBackupAsync(repo, "test-archive", sources);

        Assert.Contains("create", svc.LastArgs!);
        Assert.Contains("--progress", svc.LastArgs!);
        Assert.Contains("test-archive", svc.LastArgs!);
        Assert.Contains("/home/user/docs", svc.LastArgs!);
        Assert.Contains("/home/user/pics", svc.LastArgs!);
    }

    [Fact]
    public async Task CreateBackupAsync_WithRateLimit_IncludesFlag()
    {
        var svc = CreateService();
        var repo = LocalRepo();
        repo.RateLimit = 1000;

        await svc.CreateBackupAsync(repo, "archive", ["/data"]);

        Assert.Contains("--remote-ratelimit 1000", svc.LastArgs!);
    }

    [Fact]
    public async Task ListArchivesAsync_ProducesJsonFlag()
    {
        var svc = CreateService();
        var repo = LocalRepo();

        await svc.ListArchivesAsync(repo);

        Assert.Contains("list", svc.LastArgs!);
        Assert.Contains("--json", svc.LastArgs!);
        Assert.Contains(repo.Path, svc.LastArgs!);
    }

    [Fact]
    public async Task InfoArchiveAsync_ProducesCorrectArgs()
    {
        var svc = CreateService();
        var repo = LocalRepo();

        await svc.InfoArchiveAsync(repo, "my-archive");

        Assert.Contains("info", svc.LastArgs!);
        Assert.Contains("--json", svc.LastArgs!);
        Assert.Contains($"{repo.Path}::my-archive", svc.LastArgs!);
    }

    [Fact]
    public async Task DeleteArchiveAsync_Borg1Syntax()
    {
        var svc = CreateService();
        var repo = LocalRepo();

        await svc.DeleteArchiveAsync(repo, "old-archive");

        Assert.Contains("delete", svc.LastArgs!);
        Assert.Contains($"{repo.Path}::old-archive", svc.LastArgs!);
    }

    [Fact]
    public async Task PruneAsync_ProducesStatsAndList()
    {
        var svc = CreateService();
        var repo = LocalRepo();

        await svc.PruneAsync(repo);

        Assert.Contains("prune", svc.LastArgs!);
        Assert.Contains("--stats", svc.LastArgs!);
        Assert.Contains("--list", svc.LastArgs!);
    }

    [Fact]
    public async Task CheckAsync_ProducesProgressFlag()
    {
        var svc = CreateService();
        var repo = LocalRepo();

        await svc.CheckAsync(repo);

        Assert.Contains("check", svc.LastArgs!);
        Assert.Contains("--progress", svc.LastArgs!);
    }

    [Fact]
    public async Task CompactAsync_ProducesProgressFlag()
    {
        var svc = CreateService();
        var repo = LocalRepo();

        await svc.CompactAsync(repo);

        Assert.Contains("compact", svc.LastArgs!);
        Assert.Contains("--progress", svc.LastArgs!);
    }

    [Fact]
    public async Task ExtractAsync_WithPaths_IncludesThemInArgs()
    {
        var svc = CreateService();
        var repo = LocalRepo();

        await svc.ExtractAsync(repo, "archive", "/restore", ["dir1/file.txt", "dir2"]);

        Assert.Contains("extract", svc.LastArgs!);
        Assert.Contains("--progress", svc.LastArgs!);
        Assert.Contains("--noxattrs", svc.LastArgs!);
        Assert.Contains("dir1/file.txt", svc.LastArgs!);
        Assert.Contains("dir2", svc.LastArgs!);
    }

    [Fact]
    public async Task DiffArchivesAsync_ProducesCorrectArgs()
    {
        var svc = CreateService();
        var repo = LocalRepo();

        await svc.DiffArchivesAsync(repo, "archive1", "archive2");

        Assert.Contains("diff", svc.LastArgs!);
        Assert.Contains($"{repo.Path}::archive1", svc.LastArgs!);
        Assert.Contains("archive2", svc.LastArgs!);
    }

    [Fact]
    public async Task InfoRepoAsync_ProducesJsonFlag()
    {
        var svc = CreateService();
        var repo = LocalRepo();

        await svc.InfoRepoAsync(repo);

        Assert.Contains("info", svc.LastArgs!);
        Assert.Contains("--json", svc.LastArgs!);
    }

    /// <summary>
    /// Test subclass that captures RunCommandAsync arguments instead of executing processes.
    /// </summary>
    private class TestBorg1Service(ILogger<Borg1Service> logger, AppSettings settings, SshAgentHelper sshAgent, WslHelper wsl)
        : Borg1Service(logger, settings, sshAgent, wsl)
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
