namespace BorgMate.Services.Borg;

/// <summary>
/// Provides user-facing error messages for each BorgErrorType.
/// Uses Strings.Get() for localized messages.
/// </summary>
public static class BorgErrorMessages
{
    public static string GetMessage(BorgErrorType errorType) =>
        Strings.Get($"Error.{errorType}");
}
