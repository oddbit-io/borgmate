using BorgMate.Domain.Repositories;
using BorgMate.Runtime.Progress;

namespace BorgMate.Runtime.Operations;

/// <summary>Options for checking a remote borg binary path over SSH.</summary>
public sealed record RemotePathCheckOperationOptions(
    BorgRepository Repository,
    IOperationProgressSink ProgressSink,
    string Host,
    string RemotePath,
    string? SshKeyPath = null) : BorgOperationOptions(Repository, ProgressSink);