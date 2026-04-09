namespace BorgMate.Services.Borg;

public record BorgResult(int ExitCode, string StandardOutput, string StandardError, bool WasCancelled = false)
{
    public bool Success => ExitCode == 0;

    public BorgErrorType ErrorType => Success
        ? BorgErrorType.Unknown
        : BorgErrorClassifier.Classify(StandardError);

    /// <summary>
    /// User-friendly error message from ErrorType, falls back to raw stderr.
    /// </summary>
    public string ErrorMessage => ErrorType != BorgErrorType.Unknown
        ? BorgErrorMessages.GetMessage(ErrorType)
        : StandardError;
}
