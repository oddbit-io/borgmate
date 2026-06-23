using BorgMate.Domain.Repositories;
using BorgMate.Domain.Settings;
using BorgMate.Infrastructure.Borg;
using BorgMate.Infrastructure.Platform;
using BorgMate.Runtime.Security;
using Microsoft.Extensions.Logging;
using NSubstitute;
using WslHelper = BorgMate.Infrastructure.Platform.WslHelper;

namespace BorgMate.Tests.Infrastructure;

/// <summary>
/// Tests Borg2Service command argument construction.
/// Verifies Borg 2.0.0b21 command syntax using BORG_REPO instead of repo::archive references.
/// </summary>
public class Borg2ServiceTests
{
    private static readonly WslHelper Wsl = new(Substitute.For<ILogger<WslHelper>>());
    private static readonly SshAgentHelper SshAgent = new(Substitute.For<ILogger<SshAgentHelper>>(), null, Wsl);

    private static TestBorg2Service CreateService(AppSettings? settings = null) =>
        new(Substitute.For<ILogger<Borg2Service>>(), settings ?? new AppSettings(), SshAgent, Wsl, new PromptingPassphraseProvider(null));

    private static BorgRepository LocalRepo(string path = "/repo") =>
        new() { Name = "test", Path = path, IsLocal = true, BorgVersion = BorgVersion.Borg2 };

    [Fact]
    public async Task InitAsync_UsesRepoCreateAndBorgRepo()
    {
        var svc = CreateService();
        var repo = LocalRepo();
        repo.Borg2EncryptionMode = Borg2EncryptionMode.RepokeyBlake2AesOcb;

        await svc.InitAsync(repo);

        Assert.Equal("repo-create --encryption=repokey-blake2-aes-ocb", svc.LastArgs);
        AssertBorgRepo(svc, repo.Path);
        AssertNoObsoleteBorg2Syntax(svc.LastArgs);
    }

    [Fact]
    public async Task ListArchivesAsync_UsesRepoList()
    {
        var svc = CreateService();
        var repo = LocalRepo();

        await svc.ListArchivesAsync(repo);

        Assert.Equal("repo-list --json", svc.LastArgs);
        AssertBorgRepo(svc, repo.Path);
        AssertNoObsoleteBorg2Syntax(svc.LastArgs);
    }

    [Fact]
    public async Task DeleteArchiveAsync_UsesBareArchiveName()
    {
        var svc = CreateService();
        var repo = LocalRepo();

        await svc.DeleteArchiveAsync(repo, "old-archive");

        Assert.Equal("delete \"old-archive\"", svc.LastArgs);
        AssertBorgRepo(svc, repo.Path);
        AssertNoObsoleteBorg2Syntax(svc.LastArgs);
    }

    [Fact]
    public async Task PruneAsync_UsesBorgRepoWithoutStats()
    {
        var svc = CreateService();
        var repo = LocalRepo();

        await svc.PruneAsync(repo);

        Assert.Equal("prune --list", svc.LastArgs);
        AssertBorgRepo(svc, repo.Path);
        Assert.DoesNotContain("--stats", svc.LastArgs);
        AssertNoObsoleteBorg2Syntax(svc.LastArgs);
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
        AssertBorgRepo(svc, repo.Path);
    }

    [Fact]
    public async Task CheckAsync_UsesBorgRepo()
    {
        var svc = CreateService();
        var repo = LocalRepo();

        await svc.CheckAsync(repo);

        Assert.Equal("check --progress", svc.LastArgs);
        AssertBorgRepo(svc, repo.Path);
        AssertNoObsoleteBorg2Syntax(svc.LastArgs);
    }

    [Fact]
    public async Task CompactAsync_UsesBorgRepo()
    {
        var svc = CreateService();
        var repo = LocalRepo();

        await svc.CompactAsync(repo);

        Assert.Equal("compact --progress", svc.LastArgs);
        AssertBorgRepo(svc, repo.Path);
        AssertNoObsoleteBorg2Syntax(svc.LastArgs);
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
        AssertBorgRepo(svc, repo.Path);
        AssertNoObsoleteBorg2Syntax(svc.LastArgs);
    }

    [Fact]
    public async Task CreateBackupAsync_UsesConfiguredBorg2BinaryPath()
    {
        var settings = new AppSettings { Borg2BinaryPath = "/opt/borg2/bin/borg" };
        var svc = CreateService(settings);
        var repo = LocalRepo();

        await svc.CreateBackupAsync(repo, "archive", ["/data"]);

        Assert.Equal("/opt/borg2/bin/borg", svc.LastFileName);
    }

    [Fact]
    public async Task CreateBackupAsync_RawBorgUrl_SetsBorgRepoToRawUrl()
    {
        var svc = CreateService();
        var repo = new BorgRepository
        {
            Name = "cloud",
            BorgVersion = BorgVersion.Borg2,
            LocationKind = BorgRepositoryLocationKind.BorgUrl,
            Path = "rest://repo.example.com/backups",
            BorgUrl = "rest://repo.example.com/backups"
        };

        await svc.CreateBackupAsync(repo, "archive", ["/data"]);

        AssertBorgRepo(svc, "rest://repo.example.com/backups");
    }

    [Fact]
    public async Task CreateBackupAsync_StructuredSshFields_FormatBorg2SshUrl()
    {
        var svc = CreateService();
        var repo = new BorgRepository
        {
            Name = "remote",
            BorgVersion = BorgVersion.Borg2,
            LocationKind = BorgRepositoryLocationKind.SshFields,
            IsLocal = false,
            Path = "borg@example.com:/srv/backups",
            SshPort = 2222
        };

        await svc.CreateBackupAsync(repo, "archive", ["/data"]);

        AssertBorgRepo(svc, "ssh://borg@example.com:2222//srv/backups");
    }

    [Fact]
    public async Task InfoRepoAsync_UsesRepoInfo()
    {
        var svc = CreateService();
        var repo = LocalRepo();

        await svc.InfoRepoAsync(repo);

        Assert.Equal("repo-info --json", svc.LastArgs);
        AssertBorgRepo(svc, repo.Path);
        AssertNoObsoleteBorg2Syntax(svc.LastArgs);
    }

    [Fact]
    public async Task DiffArchivesAsync_UsesBareArchiveNames()
    {
        var svc = CreateService();
        var repo = LocalRepo();

        await svc.DiffArchivesAsync(repo, "a1", "a2");

        Assert.Equal("diff \"a1\" \"a2\"", svc.LastArgs);
        AssertBorgRepo(svc, repo.Path);
        AssertNoObsoleteBorg2Syntax(svc.LastArgs);
    }

    [Fact]
    public async Task ListArchiveContentsAsync_ProducesJsonLines()
    {
        var svc = CreateService();
        var repo = LocalRepo();

        await svc.ListArchiveContentsAsync(repo, "archive");

        Assert.Equal("list --json-lines \"archive\"", svc.LastArgs);
        AssertBorgRepo(svc, repo.Path);
        AssertNoObsoleteBorg2Syntax(svc.LastArgs);
    }

    [Fact]
    public async Task ExtractAsync_UsesDefaultCompatibilityOptions()
    {
        var svc = CreateService();
        var repo = LocalRepo();

        await svc.ExtractAsync(repo, "archive", "/restore", new BorgExtractOptions(), ["dir1/file.txt", "dir2"]);

        Assert.Contains("extract", svc.LastArgs!);
        Assert.Contains("--progress", svc.LastArgs!);
        Assert.Contains("--noflags", svc.LastArgs!);
        Assert.Contains("--noacls", svc.LastArgs!);
        Assert.Contains("--noxattrs", svc.LastArgs!);
        Assert.DoesNotContain("--sparse", svc.LastArgs!);
        Assert.Contains("dir1/file.txt", svc.LastArgs!);
        Assert.Contains("dir2", svc.LastArgs!);
        AssertBorgRepo(svc, repo.Path);
        AssertNoObsoleteBorg2Syntax(svc.LastArgs);
    }

    [Fact]
    public async Task ExtractAsync_UsesSelectedOptions()
    {
        var svc = CreateService();
        var repo = LocalRepo();
        var options = new BorgExtractOptions
        {
            NoFlags = false,
            NoAcls = true,
            NoXattrs = false,
            Sparse = true
        };

        await svc.ExtractAsync(repo, "archive", "/restore", options);

        Assert.DoesNotContain("--noflags", svc.LastArgs!);
        Assert.Contains("--noacls", svc.LastArgs!);
        Assert.DoesNotContain("--noxattrs", svc.LastArgs!);
        Assert.Contains("--sparse", svc.LastArgs!);
        AssertBorgRepo(svc, repo.Path);
    }

    [Fact]
    public async Task ChangePassphraseAsync_UsesBorgRepo()
    {
        var svc = CreateService();
        var repo = LocalRepo();
        repo.Borg2EncryptionMode = Borg2EncryptionMode.RepokeyBlake2AesOcb;

        await svc.ChangePassphraseAsync(repo, "new-pass");

        Assert.Equal("key change-passphrase", svc.LastArgs);
        AssertBorgRepo(svc, repo.Path);
        AssertNoObsoleteBorg2Syntax(svc.LastArgs);
    }

    [Fact]
    public async Task ChangePassphraseAsync_SetsBothPassphraseEnvVars()
    {
        var svc = CreateService();
        var repo = LocalRepo();
        repo.Borg2EncryptionMode = Borg2EncryptionMode.RepokeyBlake2AesOcb;
        svc.SetPassphrase(repo, "old-pass");

        await svc.ChangePassphraseAsync(repo, "new-pass");

        Assert.NotNull(svc.LastEnvVars);
        Assert.Equal("old-pass", svc.LastEnvVars!["BORG_PASSPHRASE"]);
        Assert.Equal("new-pass", svc.LastEnvVars!["BORG_NEW_PASSPHRASE"]);
        Assert.Equal(repo.Path, svc.LastEnvVars!["BORG_REPO"]);
    }

    private static void AssertBorgRepo(TestBorg2Service svc, string expectedRepo)
    {
        Assert.NotNull(svc.LastEnvVars);
        Assert.Equal(expectedRepo, svc.LastEnvVars["BORG_REPO"]);
    }

    private static void AssertNoObsoleteBorg2Syntax(string? args)
    {
        Assert.NotNull(args);
        Assert.DoesNotContain("rcreate", args);
        Assert.DoesNotContain("rlist", args);
        Assert.DoesNotContain("rinfo", args);
        Assert.DoesNotContain("--repo", args);
        Assert.DoesNotContain("::", args);
    }

    /// <summary>
    /// Test subclass that captures RunCommandAsync arguments instead of executing processes.
    /// </summary>
    private class TestBorg2Service : Borg2Service
    {
        private readonly PromptingPassphraseProvider _passphrase;

        public TestBorg2Service(
            ILogger<Borg2Service> logger,
            AppSettings settings,
            SshAgentHelper sshAgent,
            WslHelper wsl,
            PromptingPassphraseProvider passphrase)
            : base(logger, settings, sshAgent, wsl, passphrase)
        {
            _passphrase = passphrase;
        }

        public string? LastFileName { get; private set; }
        public string? LastArgs { get; private set; }
        public IReadOnlyDictionary<string, string>? LastEnvVars { get; private set; }

        public void SetPassphrase(BorgRepository repo, string value) => _passphrase.Set(repo, value);

        protected override Task<BorgResult> RunCommandAsync(
            string fileName, string arguments, BorgEnv? env = null,
            string? workingDirectory = null, CancellationToken ct = default,
            Action<string>? onStderrLine = null)
        {
            LastFileName = fileName;
            LastArgs = arguments;
            LastEnvVars = env?.Variables;
            return Task.FromResult(new BorgResult(0, "", ""));
        }
    }
}
