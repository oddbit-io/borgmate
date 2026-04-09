using BorgMate.Models;
using BorgMate.Services;
using BorgMate.Services.UI;
using BorgMate.Services.Borg;
using BorgMate.Services.Config;
using BorgMate.Services.Journal;
using BorgMate.Services.Queue;
using BorgMate.ViewModels;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace BorgMate.Tests;

public class ArchiveListViewModelTests
{
    private static readonly WslHelper Wsl = new(Substitute.For<ILogger<WslHelper>>());
    private static readonly PassphrasePrompt Prompt = new(null);
    private static readonly SshAgentHelper SshAgent = new(Substitute.For<ILogger<SshAgentHelper>>(), null, Wsl);

    private static BorgOperationRunner CreateRunner(IJournalService? journal = null) =>
        new(Substitute.For<ILogger<BorgOperationRunner>>(),
            new JobQueueService(),
            journal ?? Substitute.For<IJournalService>(),
            Prompt);

    private static ArchiveListViewModel CreateVm()
    {
        var borgFactory = new BorgServiceFactory(Substitute.For<ILoggerFactory>(), new AppSettings(), SshAgent, Wsl);
        var status = Substitute.For<IStatusService>();
        var filePicker = new FilePickerService();
        var cache = new BorgCacheService();
        var journal = Substitute.For<IJournalService>();
        var logger = Substitute.For<ILogger<ArchiveListViewModel>>();
        return new ArchiveListViewModel(borgFactory, status, filePicker, cache, journal, CreateRunner(journal), Prompt, null!, logger);
    }

    [Fact]
    public void SelectedArchive_Null_Initially()
    {
        var vm = CreateVm();
        Assert.Null(vm.SelectedArchive);
        Assert.False(vm.HasSelectedArchive);
    }

    [Fact]
    public void HasSelectedArchive_True_WhenSet()
    {
        var vm = CreateVm();
        vm.Archives.Add(new BorgArchive("test", DateTime.Now));
        vm.SelectedArchive = vm.Archives[0];

        Assert.True(vm.HasSelectedArchive);
    }

    [Fact]
    public void Repository_Changed_ClearsArchives()
    {
        var vm = CreateVm();
        vm.Archives.Add(new BorgArchive("old", DateTime.Now));
        vm.SelectedArchive = vm.Archives[0];

        vm.Repository = new BorgRepository { Name = "new", Path = "/new" };

        Assert.Empty(vm.Archives);
        Assert.Null(vm.SelectedArchive);
    }

    [Fact]
    public void Repository_Changed_CachesOldArchives()
    {
        var vm = CreateVm();
        var repo1 = new BorgRepository { Name = "repo1", Path = "/repo1" };
        var repo2 = new BorgRepository { Name = "repo2", Path = "/repo2" };

        vm.Repository = repo1;
        vm.Archives.Add(new BorgArchive("archive1", DateTime.Now));

        vm.Repository = repo2; // switches away, caching repo1's archives

        vm.Repository = repo1; // switch back, should restore from cache

        var archives = vm.Archives.ToList();
        Assert.Single(archives);
        Assert.Equal("archive1", archives[0].Name);
    }

    [Fact]
    public void InvalidateArchives_ClearsAll()
    {
        var vm = CreateVm();
        var repo = new BorgRepository { Name = "test", Path = "/test" };
        vm.Repository = repo;
        vm.Archives.Add(new BorgArchive("archive", DateTime.Now));
        vm.SelectedArchive = vm.Archives[0];

        vm.InvalidateArchives();

        Assert.Empty(vm.Archives);
        Assert.Null(vm.SelectedArchive);
    }

    [Fact]
    public void SortByName_TogglesDirection()
    {
        var vm = CreateVm();
        vm.Archives.Add(new BorgArchive("b-archive", DateTime.Now));
        vm.Archives.Add(new BorgArchive("a-archive", DateTime.Now.AddHours(-1)));

        vm.SortByNameCommand.Execute(null); // first click: ascending by name
        Assert.Equal("a-archive", vm.Archives[0].Name);
        Assert.Contains("▲", vm.NameSortIndicator);

        vm.SortByNameCommand.Execute(null); // second click: descending
        Assert.Equal("b-archive", vm.Archives[0].Name);
        Assert.Contains("▼", vm.NameSortIndicator);
    }

    [Fact]
    public void SortByDate_TogglesDirection()
    {
        var vm = CreateVm();
        var older = new BorgArchive("old", DateTime.Now.AddDays(-1));
        var newer = new BorgArchive("new", DateTime.Now);
        vm.Archives.Add(older);
        vm.Archives.Add(newer);

        // Default sort is date descending. First click toggles to ascending.
        vm.SortByDateCommand.Execute(null);
        Assert.Equal("old", vm.Archives[0].Name);
        Assert.Contains("▲", vm.DateSortIndicator);

        // Second click toggles back to descending.
        vm.SortByDateCommand.Execute(null);
        Assert.Equal("new", vm.Archives[0].Name);
        Assert.Contains("▼", vm.DateSortIndicator);
    }

    [Fact]
    public void Restore_NoRepo_ShowsError()
    {
        var status = Substitute.For<IStatusService>();
        var vm = new ArchiveListViewModel(
            new BorgServiceFactory(Substitute.For<ILoggerFactory>(), new AppSettings(), SshAgent, Wsl), status,
            new FilePickerService(), new BorgCacheService(),
            Substitute.For<IJournalService>(), CreateRunner(), Prompt,
            null!, Substitute.For<ILogger<ArchiveListViewModel>>());

        vm.RestoreCommand.Execute(null);

        status.Received().SetError(Arg.Any<string>());
    }

    [Fact]
    public void Restore_NoArchive_ShowsError()
    {
        var status = Substitute.For<IStatusService>();
        var vm = new ArchiveListViewModel(
            new BorgServiceFactory(Substitute.For<ILoggerFactory>(), new AppSettings(), SshAgent, Wsl), status,
            new FilePickerService(), new BorgCacheService(),
            Substitute.For<IJournalService>(), CreateRunner(), Prompt,
            null!, Substitute.For<ILogger<ArchiveListViewModel>>());

        vm.Repository = new BorgRepository { Name = "test", Path = "/test" };
        vm.RestoreCommand.Execute(null);

        status.Received().SetError(Arg.Any<string>());
    }

    [Fact]
    public void HasDetail_False_Initially()
    {
        var vm = CreateVm();
        Assert.False(vm.HasDetail);
    }

    [Fact]
    public void Progress_HasActiveJob_False_Initially()
    {
        var vm = CreateVm();
        Assert.False(vm.Progress.HasActiveJob);
    }
}
