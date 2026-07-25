using BorgMate.Domain.Archives;
using BorgMate.Domain.Journal;
using BorgMate.Domain.Repositories;
using BorgMate.Domain.Settings;
using BorgMate.Infrastructure.Platform;
using BorgMate.Persistence.Configuration;
using BorgMate.Persistence.Journal;
using BorgMate.Presentation.State;
using BorgMate.Runtime.Operations;
using BorgMate.Runtime.Orchestration;
using BorgMate.Runtime.Scheduling;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using OneOf;
using BackupSchedule = BorgMate.Domain.Repositories.BackupSchedule;

namespace BorgMate.Tests.Runtime;

public class SchedulerComputeNextRunTests
{
    private static BorgRepository MakeRepo(ScheduleFrequency frequency, DateTime? lastBackup = null,
        int hour = 2, int minute = 0, DayOfWeek dayOfWeek = DayOfWeek.Monday,
        int dayOfMonth = 1, int intervalHours = 6, bool runMissed = true)
    {
        return new BorgRepository
        {
            Name = "test",
            Path = "/test",
            Mode = BackupMode.Scheduled,
            SourceDirectories = ["/data"],
            LastBackupAt = lastBackup,
            Schedule = new BackupSchedule
            {
                Frequency = frequency,
                Hour = hour,
                Minute = minute,
                DayOfWeek = dayOfWeek,
                DayOfMonth = dayOfMonth,
                IntervalHours = intervalHours,
                RunMissed = runMissed
            }
        };
    }

    [Fact]
    public void Manual_ReturnsNull()
    {
        var repo = new BorgRepository { Name = "test", Path = "/test", Mode = BackupMode.Manual };
        Assert.Null(BackupScheduler.ComputeNextRun(repo));
    }

    [Fact]
    public void EveryNHours_NeverRun_ReturnsNow()
    {
        var repo = MakeRepo(ScheduleFrequency.EveryNHours, intervalHours: 6);
        var next = BackupScheduler.ComputeNextRun(repo);

        Assert.NotNull(next);
        Assert.True((DateTime.Now - next.Value).Duration() < TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void EveryNHours_NeverRun_ReturnsProvidedCurrentTime()
    {
        var now = new DateTime(2026, 7, 12, 10, 30, 0);
        var repo = MakeRepo(ScheduleFrequency.EveryNHours, intervalHours: 6);

        Assert.Equal(now, BackupScheduler.ComputeNextRun(repo, now));
    }

    [Fact]
    public void EveryNHours_LastRunRecent_ReturnsLastPlusInterval()
    {
        var lastRun = DateTime.Now.AddHours(-2);
        var repo = MakeRepo(ScheduleFrequency.EveryNHours, lastRun, intervalHours: 6);
        var next = BackupScheduler.ComputeNextRun(repo);

        Assert.NotNull(next);
        Assert.True((next.Value - lastRun.AddHours(6)).Duration() < TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Daily_ReturnsToday()
    {
        var repo = MakeRepo(ScheduleFrequency.Daily, DateTime.Now.AddDays(-2), hour: 2, minute: 30);
        var next = BackupScheduler.ComputeNextRun(repo);

        Assert.NotNull(next);
        Assert.Equal(2, next.Value.Hour);
        Assert.Equal(30, next.Value.Minute);
    }

    [Fact]
    public void Weekly_ReturnsCorrectDay()
    {
        var repo = MakeRepo(ScheduleFrequency.Weekly, DateTime.Now.AddDays(-14),
            dayOfWeek: DayOfWeek.Sunday, hour: 3);
        var next = BackupScheduler.ComputeNextRun(repo);

        Assert.NotNull(next);
        Assert.Equal(DayOfWeek.Sunday, next.Value.DayOfWeek);
        Assert.Equal(3, next.Value.Hour);
    }

    [Fact]
    public void Monthly_ReturnsCorrectDayOfMonth()
    {
        var repo = MakeRepo(ScheduleFrequency.Monthly, DateTime.Now.AddMonths(-2),
            dayOfMonth: 15, hour: 4);
        var next = BackupScheduler.ComputeNextRun(repo);

        Assert.NotNull(next);
        Assert.Equal(4, next.Value.Hour);
        // Day should be 15 or clamped to month's max
        Assert.True(next.Value.Day <= 15);
    }

    [Fact]
    public void RunMissed_False_AdvancesToFuture()
    {
        // Last run was 3 days ago, daily, RunMissed=false. The schedule hour is
        // ~12h away from now so today's occurrence never falls inside the
        // MissedRunGrace window, which would legitimately return a past time.
        var repo = MakeRepo(ScheduleFrequency.Daily, DateTime.Now.AddDays(-3),
            hour: (DateTime.Now.Hour + 12) % 24, runMissed: false);
        var next = BackupScheduler.ComputeNextRun(repo);

        Assert.NotNull(next);
        Assert.True(next.Value > DateTime.Now, "Should be in the future when RunMissed=false");
    }

    [Fact]
    public void RunMissed_False_RecentlyDue_StaysEligible()
    {
        // Due 2 minutes ago — inside the grace window: "now", not "missed".
        // Without the grace, RunMissed=false repos could never fire at all.
        var lastRun = DateTime.Now.AddHours(-2).AddMinutes(-2);
        var repo = MakeRepo(ScheduleFrequency.EveryNHours, lastRun, intervalHours: 2, runMissed: false);

        var next = BackupScheduler.ComputeNextRun(repo);

        Assert.NotNull(next);
        Assert.True(next.Value < DateTime.Now, "A just-due run must stay eligible when RunMissed=false");
    }

    [Fact]
    public void RunMissed_False_LongPastDue_Advances()
    {
        // Due 2 hours ago — well past the grace window: advanced to the next occurrence.
        var lastRun = DateTime.Now.AddHours(-3);
        var repo = MakeRepo(ScheduleFrequency.EveryNHours, lastRun, intervalHours: 1, runMissed: false);

        var next = BackupScheduler.ComputeNextRun(repo);

        Assert.NotNull(next);
        Assert.True(next.Value > DateTime.Now);
    }

    [Fact]
    public void RunMissed_True_ReturnsPastDue()
    {
        // Last run was 3 days ago, daily at midnight, RunMissed=true (default).
        // Use hour=0 so today's occurrence is always in the past regardless of current time.
        var repo = MakeRepo(ScheduleFrequency.Daily, DateTime.Now.AddDays(-3), hour: 0, minute: 0);
        var next = BackupScheduler.ComputeNextRun(repo);

        Assert.NotNull(next);
        // Past-due time should NOT be advanced, allowing immediate run
        Assert.True(next.Value < DateTime.Now);
    }

    [Fact]
    public void ComputeNextWakeRun_ChoosesNearestFutureRun()
    {
        var now = DateTime.Now;
        var soon = MakeRepo(ScheduleFrequency.EveryNHours, now.AddHours(-1), intervalHours: 2);
        var later = MakeRepo(ScheduleFrequency.EveryNHours, now.AddHours(-1), intervalHours: 4);
        var pastDue = MakeRepo(ScheduleFrequency.EveryNHours, now.AddHours(-3), intervalHours: 1);
        var manual = new BorgRepository { Name = "manual", Path = "/manual", Mode = BackupMode.Manual };

        var next = BackupScheduler.ComputeNextWakeRun([later, pastDue, manual, soon], now);

        Assert.Equal(BackupScheduler.TrimToSecond(soon.LastBackupAt!.Value.AddHours(2)), next);
    }

    [Fact]
    public void ComputeNextWakeRun_IgnoresPastDueMissedRuns()
    {
        var now = DateTime.Now;
        var pastDue = MakeRepo(ScheduleFrequency.EveryNHours, now.AddHours(-3), intervalHours: 1);

        var next = BackupScheduler.ComputeNextWakeRun([pastDue], now);

        Assert.Null(next);
    }

    [Fact]
    public async Task RefreshWakeSchedule_SchedulesNearestFutureRun()
    {
        var now = DateTime.Now;
        var repo = MakeRepo(ScheduleFrequency.EveryNHours, now.AddHours(-1), intervalHours: 2);
        var wakeScheduler = MakeWakeScheduler();
        var scheduler = CreateScheduler([repo], new AppSettings { WakeForScheduledBackups = true }, wakeScheduler);

        await scheduler.RefreshWakeScheduleAsync(true, false);

        await wakeScheduler.Received(1)
            .ScheduleAsync(BackupScheduler.TrimToSecond(repo.LastBackupAt!.Value.AddHours(2)), false, true);
    }

    [Fact]
    public async Task RefreshWakeSchedule_PassesAcOnlyFromSettings()
    {
        var now = DateTime.Now;
        var repo = MakeRepo(ScheduleFrequency.EveryNHours, now.AddHours(-1), intervalHours: 2);
        var wakeScheduler = MakeWakeScheduler();
        var settings = new AppSettings { WakeForScheduledBackups = true, ScheduledBackupsOnlyOnAcPower = false };
        var scheduler = CreateScheduler([repo], settings, wakeScheduler);

        await scheduler.RefreshWakeScheduleAsync(true, false);

        await wakeScheduler.Received(1).ScheduleAsync(Arg.Any<DateTime>(), false, false);
    }

    [Fact]
    public async Task RefreshWakeSchedule_AcOnlyToggle_ReappliesSameTime()
    {
        var now = DateTime.Now;
        var repo = MakeRepo(ScheduleFrequency.EveryNHours, now.AddHours(-1), intervalHours: 2);
        var wakeScheduler = MakeWakeScheduler();
        var settings = new AppSettings { WakeForScheduledBackups = true, ScheduledBackupsOnlyOnAcPower = true };
        var scheduler = CreateScheduler([repo], settings, wakeScheduler);

        await scheduler.RefreshWakeScheduleAsync(true, false);
        settings.ScheduledBackupsOnlyOnAcPower = false;
        await scheduler.RefreshWakeScheduleAsync(true, false);

        await wakeScheduler.Received(1).ScheduleAsync(Arg.Any<DateTime>(), Arg.Any<bool>(), Arg.Is(true));
        await wakeScheduler.Received(1).ScheduleAsync(Arg.Any<DateTime>(), Arg.Any<bool>(), Arg.Is(false));
    }

    [Fact]
    public async Task RefreshWakeSchedule_SameTarget_AppliedOnce()
    {
        var now = DateTime.Now;
        var repo = MakeRepo(ScheduleFrequency.EveryNHours, now.AddHours(-1), intervalHours: 2);
        var wakeScheduler = MakeWakeScheduler();
        var scheduler = CreateScheduler([repo], new AppSettings { WakeForScheduledBackups = true }, wakeScheduler);

        await scheduler.RefreshWakeScheduleAsync(true, false);
        await scheduler.RefreshWakeScheduleAsync(true, false);

        await wakeScheduler.Received(1).ScheduleAsync(Arg.Any<DateTime>(), Arg.Any<bool>(), Arg.Any<bool>());
    }

    [Fact]
    public async Task RefreshWakeSchedule_ClearsWhenDisabled()
    {
        var wakeScheduler = MakeWakeScheduler();
        var scheduler = CreateScheduler([], new AppSettings { WakeForScheduledBackups = false }, wakeScheduler);

        await scheduler.RefreshWakeScheduleAsync(true, false);

        await wakeScheduler.Received(1).ClearAsync(false);
    }

    [Fact]
    public async Task RefreshWakeSchedule_ClearsWhenNoFutureRuns()
    {
        var now = DateTime.Now;
        var repo = MakeRepo(ScheduleFrequency.EveryNHours, now.AddHours(-13), intervalHours: 1);
        var wakeScheduler = MakeWakeScheduler();
        var scheduler = CreateScheduler([repo], new AppSettings { WakeForScheduledBackups = true }, wakeScheduler);

        await scheduler.RefreshWakeScheduleAsync(true, false);

        await wakeScheduler.Received(1).ClearAsync(false);
    }

    [Fact]
    public async Task RefreshWakeSchedule_ClearsWhenUnsupported()
    {
        var wakeScheduler = MakeWakeScheduler(supported: false);
        var scheduler = CreateScheduler([], new AppSettings { WakeForScheduledBackups = true }, wakeScheduler);

        await scheduler.RefreshWakeScheduleAsync(true, false);

        await wakeScheduler.Received(1).ClearAsync(false);
    }

    [Fact]
    public async Task RefreshWakeSchedule_ScheduleFailure_ClearsStaleWake()
    {
        var now = DateTime.Now;
        var repo = MakeRepo(ScheduleFrequency.EveryNHours, now.AddHours(-1), intervalHours: 2);
        var wakeScheduler = MakeWakeScheduler();
        wakeScheduler.ScheduleAsync(Arg.Any<DateTime>(), Arg.Any<bool>(), Arg.Any<bool>())
            .Returns(WakeScheduleResult.Failed("boom"));
        var scheduler = CreateScheduler([repo], new AppSettings { WakeForScheduledBackups = true }, wakeScheduler);

        await scheduler.RefreshWakeScheduleAsync(true, false);

        await wakeScheduler.Received(1).ClearAsync(false);
    }

    [Fact]
    public async Task RefreshWakeSchedule_FailedTarget_NotRetriedWithoutForce()
    {
        var now = DateTime.Now;
        var repo = MakeRepo(ScheduleFrequency.EveryNHours, now.AddHours(-1), intervalHours: 2);
        var wakeScheduler = MakeWakeScheduler();
        wakeScheduler.ScheduleAsync(Arg.Any<DateTime>(), Arg.Any<bool>(), Arg.Any<bool>())
            .Returns(WakeScheduleResult.Failed("boom"));
        var scheduler = CreateScheduler([repo], new AppSettings { WakeForScheduledBackups = true }, wakeScheduler);

        await scheduler.RefreshWakeScheduleAsync(true, false);
        await scheduler.RefreshWakeScheduleAsync(false, false);

        await wakeScheduler.Received(1).ScheduleAsync(Arg.Any<DateTime>(), Arg.Any<bool>(), Arg.Any<bool>());
    }

    [Fact]
    public async Task CheckSchedules_AcOnlyEnabledOnBattery_DefersBackup()
    {
        var repo = MakeRepo(ScheduleFrequency.EveryNHours, DateTime.Now.AddHours(-3), intervalHours: 1);
        var runner = Substitute.For<IRepositoryOperationRunner>();
        var powerSource = Substitute.For<IPowerSource>();
        powerSource.IsOnBatteryAsync().Returns(true);
        var settings = new AppSettings { ScheduledBackupsOnlyOnAcPower = true };
        var scheduler = CreateScheduler([repo], settings, MakeWakeScheduler(), runner, powerSource);

        await scheduler.CheckSchedulesGuardedAsync();

        await runner.DidNotReceive().BackupAsync(Arg.Any<BackupOperationOptions>());
    }

    [Fact]
    public async Task CheckSchedules_AcOnlyEnabledOnAc_RunsBackup()
    {
        var repo = MakeRepo(ScheduleFrequency.EveryNHours, DateTime.Now.AddHours(-3), intervalHours: 1);
        var runner = Substitute.For<IRepositoryOperationRunner>();
        var powerSource = Substitute.For<IPowerSource>();
        powerSource.IsOnBatteryAsync().Returns(false);
        var settings = new AppSettings { ScheduledBackupsOnlyOnAcPower = true };
        var scheduler = CreateScheduler([repo], settings, MakeWakeScheduler(), runner, powerSource);

        await scheduler.CheckSchedulesGuardedAsync();

        await runner.Received(1).BackupAsync(Arg.Any<BackupOperationOptions>());
    }

    [Fact]
    public async Task CheckSchedules_NeverRun_RunsBackupImmediately()
    {
        var repo = MakeRepo(ScheduleFrequency.EveryNHours, intervalHours: 1);
        var runner = Substitute.For<IRepositoryOperationRunner>();
        var settings = new AppSettings { ScheduledBackupsOnlyOnAcPower = false };
        var scheduler = CreateScheduler([repo], settings, MakeWakeScheduler(), runner);

        await scheduler.CheckSchedulesGuardedAsync();

        await runner.Received(1).BackupAsync(Arg.Any<BackupOperationOptions>());
    }

    [Fact]
    public async Task CheckSchedules_SuccessfulBackup_InvalidatesArchiveCache()
    {
        var repo = MakeRepo(ScheduleFrequency.EveryNHours, DateTime.Now.AddHours(-3), intervalHours: 1);
        var store = new RepositoryPresentationState();
        store.Add(repo);
        var viewRepo = store.Repositories.Single();
        viewRepo.LoadedArchives = [new BorgArchive("cached", DateTime.Now)];
        var runner = Substitute.For<IRepositoryOperationRunner>();
        runner.BackupAsync(Arg.Any<BackupOperationOptions>())
            .Returns(Task.FromResult<OneOf<OperationOk, OperationError>>(OperationOk.Instance));
        var scheduler = CreateScheduler(store,
            new AppSettings { ScheduledBackupsOnlyOnAcPower = false }, MakeWakeScheduler(), runner);

        await scheduler.CheckSchedulesGuardedAsync();

        Assert.Null(viewRepo.LoadedArchives);
    }

    [Fact]
    public async Task CheckSchedules_FailedBackup_PreservesArchiveCache()
    {
        var repo = MakeRepo(ScheduleFrequency.EveryNHours, DateTime.Now.AddHours(-3), intervalHours: 1);
        var store = new RepositoryPresentationState();
        store.Add(repo);
        var viewRepo = store.Repositories.Single();
        viewRepo.LoadedArchives = [new BorgArchive("cached", DateTime.Now)];
        var runner = Substitute.For<IRepositoryOperationRunner>();
        runner.BackupAsync(Arg.Any<BackupOperationOptions>())
            .Returns(Task.FromResult<OneOf<OperationOk, OperationError>>(
                new OperationError.Validation("failed")));
        var scheduler = CreateScheduler(store,
            new AppSettings { ScheduledBackupsOnlyOnAcPower = false }, MakeWakeScheduler(), runner);

        await scheduler.CheckSchedulesGuardedAsync();

        Assert.NotNull(viewRepo.LoadedArchives);
    }

    [Fact]
    public async Task CheckSchedules_FailedBackup_IsSuppressedUntilCooldownExpires()
    {
        var now = DateTime.Now;
        var repo = MakeRepo(ScheduleFrequency.EveryNHours, now.AddHours(-3), intervalHours: 1);
        var runner = Substitute.For<IRepositoryOperationRunner>();
        runner.BackupAsync(Arg.Any<BackupOperationOptions>())
            .Returns(Task.FromResult<OneOf<OperationOk, OperationError>>(
                new OperationError.Validation("failed")));
        var scheduler = CreateScheduler([repo],
            new AppSettings { ScheduledBackupsOnlyOnAcPower = false }, MakeWakeScheduler(), runner,
            getCurrentTime: () => now);

        await scheduler.CheckSchedulesGuardedAsync();
        await scheduler.CheckSchedulesGuardedAsync();

        await runner.Received(1).BackupAsync(Arg.Any<BackupOperationOptions>());
    }

    [Fact]
    public async Task CheckSchedules_FailedBackup_IsRetriedAfterCooldownExpires()
    {
        var now = DateTime.Now;
        var repo = MakeRepo(ScheduleFrequency.EveryNHours, now.AddHours(-3), intervalHours: 1);
        var runner = Substitute.For<IRepositoryOperationRunner>();
        runner.BackupAsync(Arg.Any<BackupOperationOptions>())
            .Returns(Task.FromResult<OneOf<OperationOk, OperationError>>(
                new OperationError.Validation("failed")));
        var scheduler = CreateScheduler([repo],
            new AppSettings { ScheduledBackupsOnlyOnAcPower = false }, MakeWakeScheduler(), runner,
            getCurrentTime: () => now);

        await scheduler.CheckSchedulesGuardedAsync();
        now += BackupScheduler.FailedBackupRetryCooldown;
        await scheduler.CheckSchedulesGuardedAsync();

        await runner.Received(2).BackupAsync(Arg.Any<BackupOperationOptions>());
    }

    [Fact]
    public async Task CheckSchedules_BatteryDeferral_JournaledOncePerEpisode()
    {
        var repo = MakeRepo(ScheduleFrequency.EveryNHours, DateTime.Now.AddHours(-3), intervalHours: 1);
        var powerSource = Substitute.For<IPowerSource>();
        powerSource.IsOnBatteryAsync().Returns(true);
        var journal = Substitute.For<IJournalStore>();
        journal.Add(Arg.Any<JournalEventKind>(), Arg.Any<object[]?>(), Arg.Any<string?>())
            .Returns(new JournalEntry(JournalEventKind.BackupDeferred));
        var settings = new AppSettings { ScheduledBackupsOnlyOnAcPower = true };
        var scheduler = CreateScheduler([repo], settings, MakeWakeScheduler(),
            powerSource: powerSource, journal: journal);

        // Two consecutive battery passes → a single journal entry + notification.
        await scheduler.CheckSchedulesGuardedAsync();
        await scheduler.CheckSchedulesGuardedAsync();

        journal.Received(1).Add(JournalEventKind.BackupDeferred, Arg.Any<object[]?>(), "test");
        journal.Received(1).Complete(Arg.Any<JournalEntry>(), JournalResult.Deferred,
            Arg.Is<string?>(s => s != null && s.Contains("test")));

        // Power returns and a run proceeds → the next battery episode is reported again.
        powerSource.IsOnBatteryAsync().Returns(false);
        await scheduler.CheckSchedulesGuardedAsync();
        powerSource.IsOnBatteryAsync().Returns(true);
        await scheduler.CheckSchedulesGuardedAsync();

        journal.Received(2).Add(JournalEventKind.BackupDeferred, Arg.Any<object[]?>(), Arg.Any<string?>());
    }

    [Fact]
    public async Task CheckSchedules_AcOnlyDisabledOnBattery_RunsBackup()
    {
        var repo = MakeRepo(ScheduleFrequency.EveryNHours, DateTime.Now.AddHours(-3), intervalHours: 1);
        var runner = Substitute.For<IRepositoryOperationRunner>();
        var powerSource = Substitute.For<IPowerSource>();
        powerSource.IsOnBatteryAsync().Returns(true);
        var settings = new AppSettings { ScheduledBackupsOnlyOnAcPower = false };
        var scheduler = CreateScheduler([repo], settings, MakeWakeScheduler(), runner, powerSource);

        await scheduler.CheckSchedulesGuardedAsync();

        await runner.Received(1).BackupAsync(Arg.Any<BackupOperationOptions>());
    }

    [Fact]
    public async Task CheckSchedules_MarksRepoBusyAfterBackupOperationStarts()
    {
        var repo = MakeRepo(ScheduleFrequency.EveryNHours, DateTime.Now.AddHours(-3), intervalHours: 1);
        var store = new RepositoryPresentationState();
        store.Add(repo);
        var viewRepo = store.Repositories.Single();
        var runner = Substitute.For<IRepositoryOperationRunner>();
        var wasBusyWhenBackupStarted = true;
        runner.BackupAsync(Arg.Any<BackupOperationOptions>())
            .Returns(_ =>
            {
                wasBusyWhenBackupStarted = viewRepo.IsBusy;
                return Task.FromResult<OneOf<OperationOk, OperationError>>(OperationOk.Instance);
            });
        var settings = new AppSettings { ScheduledBackupsOnlyOnAcPower = false };
        var scheduler = CreateScheduler(store, settings, MakeWakeScheduler(), runner);

        await scheduler.CheckSchedulesGuardedAsync();

        Assert.False(wasBusyWhenBackupStarted);
        Assert.False(viewRepo.IsBusy);
    }

    private static IWakeScheduler MakeWakeScheduler(bool supported = true)
    {
        var wakeScheduler = Substitute.For<IWakeScheduler>();
        wakeScheduler.IsSupported.Returns(supported);
        wakeScheduler.ScheduleAsync(Arg.Any<DateTime>(), Arg.Any<bool>(), Arg.Any<bool>()).Returns(WakeScheduleResult.Ok());
        wakeScheduler.ClearAsync(Arg.Any<bool>()).Returns(WakeScheduleResult.Ok());
        return wakeScheduler;
    }

    private static BackupScheduler CreateScheduler(
        IEnumerable<BorgRepository> repositories,
        AppSettings settings,
        IWakeScheduler wakeScheduler,
        IRepositoryOperationRunner? repositoryRunner = null,
        IPowerSource? powerSource = null,
        IJournalStore? journal = null,
        Func<DateTime>? getCurrentTime = null)
    {
        var store = new RepositoryPresentationState();
        foreach (var repository in repositories)
            store.Add(repository);

        return CreateScheduler(store, settings, wakeScheduler, repositoryRunner, powerSource, journal, getCurrentTime);
    }

    private static BackupScheduler CreateScheduler(
        RepositoryPresentationState store,
        AppSettings settings,
        IWakeScheduler wakeScheduler,
        IRepositoryOperationRunner? repositoryRunner = null,
        IPowerSource? powerSource = null,
        IJournalStore? journal = null,
        Func<DateTime>? getCurrentTime = null)
    {
        return new BackupScheduler(
            NullLogger<BackupScheduler>.Instance,
            store,
            repositoryRunner ?? Substitute.For<IRepositoryOperationRunner>(),
            Substitute.For<ISettingsStore>(),
            settings,
            wakeScheduler,
            powerSource ?? Substitute.For<IPowerSource>(),
            journal ?? Substitute.For<IJournalStore>(),
            getCurrentTime ?? (() => DateTime.Now));
    }
}
