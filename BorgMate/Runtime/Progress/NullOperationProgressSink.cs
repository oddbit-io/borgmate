namespace BorgMate.Runtime.Progress;

/// <summary>Progress sink used when a caller does not need runtime progress updates.</summary>
public sealed class NullOperationProgressSink : IOperationProgressSink
{
    private NullOperationProgressSink()
    {
    }

    /// <summary>A reusable no-op progress sink.</summary>
    public static NullOperationProgressSink Instance { get; } = new();

    /// <inheritdoc />
    public void Report(OperationProgressUpdate update)
    {
    }
}