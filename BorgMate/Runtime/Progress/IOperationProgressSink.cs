namespace BorgMate.Runtime.Progress;

/// <summary>Receives neutral progress updates from runtime operations.</summary>
public interface IOperationProgressSink
{
    /// <summary>Reports the latest operation progress state.</summary>
    void Report(OperationProgressUpdate update);
}