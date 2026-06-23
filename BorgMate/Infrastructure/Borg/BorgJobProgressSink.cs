using Avalonia.Threading;
using BorgMate.Runtime.Jobs;
using BorgMate.Runtime.Progress;

namespace BorgMate.Infrastructure.Borg;

/// <summary>Bridges neutral runtime progress updates into the existing job presentation model.</summary>
internal sealed class BorgJobProgressSink(BorgJob job, IOperationProgressSink? inner = null) : IOperationProgressSink
{
    public void Report(OperationProgressUpdate update)
    {
        inner?.Report(update);

        Dispatcher.UIThread.Post(() =>
        {
            if (update.Percent is { } percent)
            {
                job.Progress = percent;
                job.RecordProgress(percent);
            }

            if (update.Retry is { } retry)
            {
                job.StatusMessage = update.StatusKey switch
                {
                    "Status.RetryingIn" => string.Format(
                        Strings.Get("Status.RetryingIn"),
                        (int)retry.RemainingDelay.TotalSeconds),
                    "Status.RetryAttempt" => string.Format(
                        Strings.Get("Status.RetryAttempt"),
                        retry.Attempt,
                        retry.MaxAttempts),
                    _ => job.StatusMessage
                };

                if (update.StatusKey == "Status.RetryAttempt")
                    job.Progress = null;
            }
            else if (!string.IsNullOrWhiteSpace(update.StatusKey))
            {
                job.StatusMessage = Strings.Get(update.StatusKey);
            }
        });
    }
}