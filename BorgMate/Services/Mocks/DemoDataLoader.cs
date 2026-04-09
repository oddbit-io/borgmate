#if DEBUG
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BorgMate.Models;
using BorgMate.Services.Config;
using BorgMate.Services.Journal;
using BorgMate.ViewModels;
namespace BorgMate.Services.Mocks;

public static class DemoDataLoader
{
    public static void Load(MainWindowViewModel mainVm, IJournalService journal, StatusService status, Func<string, List<UpdateService.ChangelogEntry>, Task<bool>> showChangelog)
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
                Schedule = new BackupSchedule { Frequency = ScheduleFrequency.Weekly, Hour = 3, Minute = 0, DayOfWeek = DayOfWeek.Sunday }
            },
            new() { Name = "NAS Archive", Path = "/volume1/backups/borg", IsLocal = true, EncryptionMode = BorgEncryptionMode.None }
        };

        mainVm.RepositoryList.Repositories.ReplaceWith(repos);
        mainVm.RepositoryList.SelectedRepository = repos[0];
        mainVm.RepositoryList.Stats.TotalSize = Localization.Strings.FormatBytes(48_372_015_104);
        mainVm.RepositoryList.Stats.TotalChunks = (285_413).ToString("N0", Localization.Strings.Culture);

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
        mainVm.ArchiveList.Archives.ReplaceWith(archives);
        mainVm.ArchiveList.SelectedArchive = archives[0];
        mainVm.ArchiveList.DetailOriginalSize = Localization.Strings.FormatBytes(2_469_606_195);
        mainVm.ArchiveList.DetailFileCount = (1_247).ToString("N0", Localization.Strings.Culture);

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

        var fakeVersion = "99.0.0";
        status.SetUpdateAvailable(
            string.Format(Strings.Get("UpdateAvailable"), fakeVersion),
            async () =>
            {
                var changelog = new List<UpdateService.ChangelogEntry>
                {
                    new("99.0.0", "2026-04-10",
                    [
                        "Scheduled backups with configurable frequency",
                        "Native OS notifications on operation completion",
                        "Show changelog before downloading update"
                    ]),
                    new("98.0.0", "2026-04-05",
                    [
                        "Fix UI freeze when opening modal dialogs",
                        "Atomic config writes to prevent data loss",
                        "Cap stdout/stderr at 10K lines per job"
                    ])
                };
                if (!await showChangelog(fakeVersion, changelog))
                    return;
                await DialogHelper.ConfirmAsync(Strings.Get("RestartToUpdate"));
            });
    }
}
#endif
