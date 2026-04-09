namespace BorgMate.Services.Config;

public interface IStatusService
{
    /// <summary>Shows an error dialog with the given message.</summary>
    void SetError(string message);

    /// <summary>Shows an error dialog with repo context formatted into the message.</summary>
    void SetError(string message, string repoName, string repoPath);
}
