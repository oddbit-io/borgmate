using BorgMate.Domain.Repositories;
using BorgMate.Runtime.Progress;

namespace BorgMate.Runtime.Operations;

/// <summary>Options for checking a remote borg binary path over SSH.</summary>
public sealed record RemotePathCheckOperationOptions : BorgOperationOptions
{
    public RemotePathCheckOperationOptions(
        BorgRepository repository,
        IOperationProgressSink progressSink,
        string host,
        string remotePath,
        string? sshKeyPath = null,
        int sshPort = 22)
        : base(repository, progressSink)
    {
        Host = host;
        RemotePath = remotePath;
        SshKeyPath = sshKeyPath;
        SshPort = sshPort;
    }

    public string Host { get; init; }

    public string RemotePath { get; init; }

    public string? SshKeyPath { get; init; }

    public int SshPort { get; init; }
}
