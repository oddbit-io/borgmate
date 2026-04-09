namespace BorgMate.Services.Borg;

public enum BorgErrorType
{
    Unknown,

    // SSH errors
    SshHostKeyVerificationFailed,
    SshPermissionDenied,
    SshConnectionRefused,
    SshConnectionTimedOut,
    SshNoRouteToHost,
    SshHostnameNotFound,
    SshConnectionReset,
    SshKeyFileNotFound,

    // Borg errors
    RepositoryNotFound,
    RepositoryAlreadyExists,
    WrongPassphrase,
    RepositoryLocked,
    InvalidRepository,
    IncompatibleRepository,
    NoSpaceLeftOnDevice,
    BorgBinaryNotFound,
    BinaryNotFound,
    OperationCancelled,
}
