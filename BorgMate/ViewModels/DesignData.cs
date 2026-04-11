using System;
using System.Collections.ObjectModel;
using BorgMate.Models;
using BorgMate.Services.Mocks;

namespace BorgMate.ViewModels;

public static class DesignData
{
    private static readonly MockJournalService Journal = CreateJournalService();

    private static readonly BorgRepository[] SampleRepos =
    [
        new BorgRepository
        {
            Name = "Local Backup", Path = "/mnt/backup/borg-repo",
            IsLocal = true, BorgVersion = BorgVersion.Borg1,
            EncryptionMode = BorgEncryptionMode.RepokeyBlake2,
            Mode = BackupMode.Scheduled,
            LastBackupAt = DateTime.Now.AddHours(-6),
            Schedule = new BackupSchedule { Frequency = ScheduleFrequency.Daily, Hour = 2, Minute = 0 }
        },
        new BorgRepository
        {
            Name = "Remote Server", Path = "user@backup.example.com:/data/borg",
            IsLocal = false, SshPort = 2222, BorgVersion = BorgVersion.Borg1,
            EncryptionMode = BorgEncryptionMode.RepokeyBlake2,
            SshKeyPath = "~/.ssh/id_ed25519",
            Mode = BackupMode.Scheduled,
            LastBackupAt = DateTime.Now.AddDays(-1),
            Schedule = new BackupSchedule { Frequency = ScheduleFrequency.Weekly, Hour = 3, Minute = 0, DayOfWeek = DayOfWeek.Sunday }
        },
        new BorgRepository
        {
            Name = "NAS Archive", Path = "/volume1/backups/borg",
            IsLocal = true, BorgVersion = BorgVersion.Borg1,
            EncryptionMode = BorgEncryptionMode.None
        }
    ];

    private static readonly BorgArchive[] SampleArchives =
    [
        new("daily-2026-03-27T14-00-00", DateTime.Now.AddHours(-2)),
        new("daily-2026-03-26T14-00-00", DateTime.Now.AddDays(-1)),
        new("daily-2026-03-25T14-00-00", DateTime.Now.AddDays(-2)),
        new("weekly-2026-03-23T03-00-00", DateTime.Now.AddDays(-4)),
        new("daily-2026-03-22T14-00-00", DateTime.Now.AddDays(-5)),
        new("monthly-2026-03-01T03-00-00", DateTime.Now.AddDays(-26))
    ];

    public static RepositoriesPageViewModel RepositoriesPage
    {
        get
        {
            var vm = new RepositoriesPageViewModel();
            SampleRepos[0].SourceDirectories.Add("/home/user/Documents");
            SampleRepos[0].SourceDirectories.Add("/home/user/Photos");
            foreach (var repo in SampleRepos)
                vm.Repositories.Add(repo);
            vm.SelectedRepository = SampleRepos[0];
            foreach (var archive in SampleArchives)
                vm.Archives.Add(archive);
            vm.SelectedArchive = SampleArchives[0];
            vm.DetailOriginalSize = "2.3 GB";
            vm.DetailFileCount = "1,247";
            return vm;
        }
    }

    public static NotificationsViewModel NotificationsView
    {
        get
        {
            var vm = new NotificationsViewModel(Journal);
            return vm;
        }
    }

    public static MainWindowViewModel MainWindow
    {
        get
        {
            var vm = new MainWindowViewModel
            {
                IsSidebarExpanded = true
            };
            return vm;
        }
    }

    public static BrowseArchiveViewModel BrowseArchive
    {
        get
        {
            var vm = new BrowseArchiveViewModel { Title = "Browse: daily-2026-03-27T14-00-00", StatusText = "3 files, 1.5 MiB" };
            var home = new ArchiveFileNode { Name = "home", FullPath = "home", IsDirectory = true, Depth = 0 };
            var user = new ArchiveFileNode { Name = "user", FullPath = "home/user", IsDirectory = true, Parent = home, Depth = 1 };
            home.Children.Add(user);
            var docs = new ArchiveFileNode { Name = "Documents", FullPath = "home/user/Documents", IsDirectory = true, Parent = user, Depth = 2 };
            var photos = new ArchiveFileNode { Name = "Photos", FullPath = "home/user/Photos", IsDirectory = true, Parent = user, Depth = 2 };
            user.Children.Add(docs);
            user.Children.Add(photos);
            docs.Children.Add(new ArchiveFileNode { Name = "report.pdf", FullPath = "home/user/Documents/report.pdf", Size = 1_048_576, Parent = docs, Depth = 3 });
            docs.Children.Add(new ArchiveFileNode { Name = "notes.txt", FullPath = "home/user/Documents/notes.txt", Size = 4096, Parent = docs, Depth = 3 });
            photos.Children.Add(new ArchiveFileNode { Name = "vacation.jpg", FullPath = "home/user/Photos/vacation.jpg", Size = 524_288, Parent = photos, Depth = 3 });
            photos.Children.Add(new ArchiveFileNode { Name = "family.png", FullPath = "home/user/Photos/family.png", Size = 2_097_152, Parent = photos, Depth = 3 });
            home.IsExpanded = true;
            user.IsExpanded = true;
            vm.FlatNodes.Add(home);
            vm.FlatNodes.Add(user);
            vm.FlatNodes.Add(docs);
            vm.FlatNodes.Add(photos);
            return vm;
        }
    }

    private static MockJournalService CreateJournalService()
    {
        var svc = new MockJournalService();
        svc.DirectAdd(new JournalEntry(JournalEventKind.Backup, ["Documents Backup"],
            "1,247 files, 2.3 GB uploaded", "Local Backup",
            completedAt: DateTime.Now.AddMinutes(-5), result: JournalResult.Completed));
        svc.DirectAdd(new JournalEntry(JournalEventKind.Backup, ["Server Config"],
            "ssh: connect to host backup.example.com port 2222: Connection refused",
            "Remote Server",
            completedAt: DateTime.Now.AddHours(-1), result: JournalResult.Failed));
        svc.DirectAdd(new JournalEntry(JournalEventKind.Delete, ["daily-2026-03-25"],
            repositoryName: "Local Backup",
            completedAt: DateTime.Now.AddHours(-2), result: JournalResult.Completed));
        svc.DirectAdd(new JournalEntry(JournalEventKind.Backup, ["Media Archive"],
            repositoryName: "NAS Archive",
            completedAt: DateTime.Now.AddHours(-3), result: JournalResult.Cancelled));
        svc.DirectAdd(new JournalEntry(JournalEventKind.Create, ["NAS Archive"],
            repositoryName: "NAS Archive",
            completedAt: DateTime.Now.AddDays(-1), result: JournalResult.Completed));
        return svc;
    }
}
