#if DEBUG
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BorgMate.Domain.Archives;
using BorgMate.Domain.Journal;
using BorgMate.Domain.Repositories;
using BorgMate.Infrastructure.Updates;
using BorgMate.Persistence.Journal;
using BorgMate.Presentation.Dialogs;
using BorgMate.Presentation.ViewModels;

namespace BorgMate.Presentation.State;

public static class DemoDataLoader
{
    public static void Load(MainWindowViewModel mainVm, IJournalStore journal, UpdateStatusState status,
        Func<string, List<ChangelogEntry>, Task<bool>> showChangelog, bool showUpdate)
    {
        var repos = new BorgRepository[]
        {
            new()
            {
                Name = "Local Backup", Path = "/mnt/backup/borg-repo", IsLocal = true,
                EncryptionMode = BorgEncryptionMode.RepokeyBlake2,
                Mode = BackupMode.Scheduled, LastBackupAt = DateTime.Now.AddHours(-6),
                Schedule = new BackupSchedule { Frequency = ScheduleFrequency.Daily, Hour = 2, Minute = 0 }
            },
            new()
            {
                Name = "Remote Server", Path = "user@backup.example.com:/data/borg", IsLocal = false,
                SshPort = 2222, SshKeyPath = "~/.ssh/id_ed25519",
                EncryptionMode = BorgEncryptionMode.RepokeyBlake2,
                Mode = BackupMode.Scheduled, LastBackupAt = DateTime.Now.AddDays(-1),
                Schedule = new BackupSchedule
                    { Frequency = ScheduleFrequency.Weekly, Hour = 3, Minute = 0, DayOfWeek = DayOfWeek.Sunday }
            },
            new()
            {
                Name = "NAS Archive", Path = "/volume1/backups/borg", IsLocal = true,
                EncryptionMode = BorgEncryptionMode.None
            }
        };

        var repoVms = repos.Select(repo => new RepositoryViewModel(repo)).ToArray();
        mainVm.RepositoriesPage.Repositories.ReplaceWith(repoVms);
        mainVm.RepositoriesPage.SelectedRepository = repoVms[0];
        repoVms[0].StatsTotalSize = 48_372_015_104;
        repoVms[0].StorageSpace = new StorageSpaceSnapshot
        {
            AvailableBytes = 1_298_374_651_904,
            CapacityBytes = 2_000_398_934_016
        };

        repos[0].SourceDirectories.Add("/Users/user/Documents");
        repos[0].SourceDirectories.Add("/Users/user/Photos");
        repos[1].SourceDirectories.Add("/etc");
        repos[1].SourceDirectories.Add("/var/log");
        repos[2].SourceDirectories.Add("/Users/user/Music");
        repos[2].SourceDirectories.Add("/Users/user/Videos");

        var archives = new BorgArchive[]
        {
            new("daily-2026-03-27T14-00-00", DateTime.Now.AddHours(-2)),
            new("daily-2026-03-26T14-00-00", DateTime.Now.AddDays(-1)),
            new("daily-2026-03-25T14-00-00", DateTime.Now.AddDays(-2)),
            new("weekly-2026-03-23T03-00-00", DateTime.Now.AddDays(-4)),
            new("daily-2026-03-22T14-00-00", DateTime.Now.AddDays(-5)),
            new("monthly-2026-03-01T03-00-00", DateTime.Now.AddDays(-26))
        };
        mainVm.RepositoriesPage.Archives.ReplaceWith(archives);
        mainVm.RepositoriesPage.SelectedArchive = archives[0];
        mainVm.RepositoriesPage.DetailOriginalSize = Strings.FormatBytes(2_469_606_195);
        mainVm.RepositoriesPage.DetailFileCount = 1_247.ToString("N0", Strings.Culture);

        journal.ClearFinished();

        var rng = new Random(42);
        var repoNames = new[] { "Local Backup", "Remote Server", "NAS Archive" };
        var operations = new[]
        {
            (JournalEventKind.Backup, JournalResult.Completed),
            (JournalEventKind.Backup, JournalResult.Failed),
            (JournalEventKind.Backup, JournalResult.Cancelled),
            (JournalEventKind.Check, JournalResult.Completed),
            (JournalEventKind.Compact, JournalResult.Completed),
            (JournalEventKind.Delete, JournalResult.Completed),
            (JournalEventKind.Create, JournalResult.Completed)
        };
        var errors = new[] { "Connection refused", "Permission denied", "Repository locked", "Disk full" };

        for (var i = 0; i < 1000; i++)
        {
            var (kind, result) = operations[rng.Next(operations.Length)];
            var repo = repoNames[rng.Next(repoNames.Length)];
            var startedAt = DateTime.Now.AddMinutes(-(i * 15 + rng.Next(10)));
            var completedAt = startedAt.AddSeconds(rng.Next(5, 300));

            var args = kind == JournalEventKind.Delete
                ? new object[] { $"daily-{startedAt:yyyy-MM-dd}" }
                : new object[] { repo };

            var detail = result == JournalResult.Completed && kind == JournalEventKind.Backup
                ? $"{rng.Next(100, 10000)} files, {rng.Next(1, 50)}.{rng.Next(0, 9)} GB uploaded"
                : result == JournalResult.Failed
                    ? $"borg: Error: {errors[rng.Next(errors.Length)]}"
                    : null;

            journal.DirectAdd(new JournalEntry(kind, args, detail, repo, startedAt, completedAt, result));
        }

        mainVm.IsSidebarExpanded = true;

        if (!showUpdate)
            return;

        var fakeVersion = "99.0.0";
        status.SetUpdateAvailable(
            string.Format(Strings.Get("UpdateAvailable"), fakeVersion),
            async () =>
            {
                var changelog = new List<ChangelogEntry>
                {
                    new()
                    {
                        Version = "99.0.0",
                        Date = "2026-04-10",
                        Changes = new ChangelogChanges
                        {
                            Features =
                            [
                                "Scheduled backups with configurable frequency",
                                "Native OS notifications on operation completion"
                            ],
                            Improvements = ["Show changelog before downloading update"]
                        }
                    },
                    new()
                    {
                        Version = "98.0.0",
                        Date = "2026-04-05",
                        Changes = new ChangelogChanges
                        {
                            Improvements =
                            [
                                "Atomic config writes to prevent data loss",
                                "Cap stdout/stderr at 10K lines per job"
                            ],
                            Fixes = ["Fix UI freeze when opening modal dialogs"]
                        }
                    }
                };
                if (!await showChangelog(fakeVersion, changelog))
                    return;
                await DialogHelper.ConfirmAsync(Strings.Get("RestartToUpdate"));
            });
    }
}
#endif
