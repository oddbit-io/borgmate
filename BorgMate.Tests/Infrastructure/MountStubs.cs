namespace BorgMate.Tests.Infrastructure;

/// <summary>Stub mkdir/mount/mountpoint programs on a PATH-prepended directory; they log their argv.</summary>
internal class MountStubs : IDisposable
{
    private const string LogVariable = "STUB_LOG";
    private const string LoggingStub = "#!/bin/bash\nprintf '%s\\n' \"${0##*/} $*\" >> \"$STUB_LOG\"\n";
    private const string FailingStub = "#!/bin/bash\nexit 1\n";
    private readonly TempDirectory _temp = new();

    public MountStubs()
    {
        var stubDirectory = RootPath + "/stubs";
        Directory.CreateDirectory(stubDirectory);
        WriteStub(stubDirectory, "mkdir", LoggingStub);
        WriteStub(stubDirectory, "mount", LoggingStub);
        WriteStub(stubDirectory, "mountpoint", FailingStub);

        EnvironmentVariables = new Dictionary<string, string>
        {
            [LogVariable] = LogPath,
            ["PATH"] = stubDirectory + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH")
        };
    }

    public string RootPath => _temp.Path;

    public IReadOnlyDictionary<string, string> EnvironmentVariables { get; }

    private string LogPath => RootPath + "/stub.log";

    public string[] ReadLog()
    {
        return File.ReadAllLines(LogPath);
    }

    public void Dispose()
    {
        _temp.Dispose();
    }

    private static void WriteStub(string directory, string name, string content)
    {
        var path = directory + "/" + name;
        File.WriteAllText(path, content);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }
}
