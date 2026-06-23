using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using BorgMate.Domain.Journal;
using BorgMate.Infrastructure.Borg;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BorgMate.Runtime.Jobs;

public partial class BorgJob : ObservableObject
{
    // ETA via exponential moving average of progress rate
    private const double EmaAlpha = 0.15;

    private const int MaxOutputLines = 10_000;

    /// <summary>
    ///     AsyncLocal context so BorgServiceBase can record command details on the current job.
    /// </summary>
    public static readonly AsyncLocal<BorgJob?> Current = new();

    // Process details (populated by BorgServiceBase via AsyncLocal)
    [ObservableProperty]
    private string? _commandLine;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TimingDisplay))]
    private DateTime? _completedAt;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TimingDisplay))]
    private string _elapsedDisplay = string.Empty;

    private bool _emaInitialized;

    [ObservableProperty]
    private string? _environmentDisplay;

    private DateTime _lastProgressTime;
    private double _lastProgressValue;

    [ObservableProperty]
    private string _name = string.Empty;

    /// <summary>
    ///     Progress percentage (0–100), or null for indeterminate.
    /// </summary>
    [ObservableProperty]
    private double? _progress;

    private double _smoothedRate; // percent per second

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TimingDisplay))]
    private DateTime? _startedAt;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TimingDisplay))]
    private BorgJobStatus _status = BorgJobStatus.Pending;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public Guid Id { get; } = Guid.NewGuid();

    public BorgJobKind Kind { get; init; }

    /// <summary>
    ///     Repository path for cancelling related jobs on passphrase failure.
    /// </summary>
    public string? RepoPath { get; init; }

    /// <summary>
    ///     Optional deduplication key. If set, BorgJobQueue prevents enqueuing
    ///     a duplicate job with the same tag while one is pending or running.
    /// </summary>
    public string? Tag { get; init; }

    /// <summary>
    ///     Total expected size in bytes for progress calculation. Set before job starts.
    /// </summary>
    public long TotalSize { get; set; }

    /// <summary>
    ///     Total expected file count for progress display. Set before job starts.
    /// </summary>
    public long TotalFileCount { get; set; }

    /// <summary>
    ///     Status label shown during percentage-based progress (check, compact).
    ///     Set before job starts; read by BorgProgressParser.
    /// </summary>
    public string? ProgressLabel { get; set; }

    public ObservableCollection<string> StandardOutput { get; } = [];
    public ObservableCollection<string> StandardError { get; } = [];

    public string TimingDisplay
    {
        get
        {
            if (StartedAt is null) return string.Empty;
            var started = $"Started at {StartedAt.Value:HH:mm:ss}";
            if (CompletedAt is not null)
                return $"{started} · completed in {ElapsedDisplay}";
            return started;
        }
    }

    /// <summary>Linked journal entry for activity tracking.</summary>
    public JournalEntry? JournalEntry { get; set; }

    public CancellationTokenSource Cts { get; } = new();
    public TaskCompletionSource<BorgResult> Completion { get; } = new();

    public Func<BorgJob, CancellationToken, IProgress<string>, Task<BorgResult>>? Work { get; init; }

    /// <summary>
    ///     Records a progress sample for EMA-based ETA/speed estimation.
    ///     Called by BorgProgressParser as borg reports progress on stderr.
    /// </summary>
    public void RecordProgress(double progress)
    {
        var now = DateTime.Now;
        if (!_emaInitialized)
        {
            _lastProgressTime = now;
            _lastProgressValue = progress;
            _emaInitialized = true;
            return;
        }

        var dt = (now - _lastProgressTime).TotalSeconds;
        if (dt < 0.5) return; // skip rapid-fire updates

        var dp = progress - _lastProgressValue;
        _lastProgressTime = now;
        _lastProgressValue = progress;

        if (dp <= 0 || dt <= 0) return;

        var instantRate = dp / dt;
        _smoothedRate = _smoothedRate > 0
            ? EmaAlpha * instantRate + (1 - EmaAlpha) * _smoothedRate
            : instantRate; // first valid rate — use as-is
    }

    /// <summary>
    ///     Returns estimated throughput in bytes/sec based on EMA-smoothed progress rate.
    /// </summary>
    public long? EstimateSpeed()
    {
        if (_smoothedRate <= 0 || TotalSize <= 0 || Progress is not (> 0 and < 100)) return null;
        return (long)(_smoothedRate / 100 * TotalSize);
    }

    /// <summary>
    ///     Returns estimated remaining time based on EMA-smoothed progress rate.
    /// </summary>
    public TimeSpan? EstimateRemaining()
    {
        if (_smoothedRate <= 0 || Progress is not (> 0 and < 100)) return null;
        var remaining = (100 - Progress.Value) / _smoothedRate;
        return TimeSpan.FromSeconds(remaining);
    }

    public void AppendStdout(string line)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (StandardOutput.Count >= MaxOutputLines)
                StandardOutput.RemoveAt(0);
            StandardOutput.Add(line);
        });
    }

    public void AppendStderr(string line)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (StandardError.Count >= MaxOutputLines)
                StandardError.RemoveAt(0);
            StandardError.Add(line);
        });
    }

    public void UpdateElapsed()
    {
        if (StartedAt is null) return;
        var end = CompletedAt ?? DateTime.Now;
        var elapsed = end - StartedAt.Value;
        ElapsedDisplay = elapsed.TotalHours >= 1
            ? elapsed.ToString(@"h\:mm\:ss")
            : elapsed.ToString(@"m\:ss");
    }
}