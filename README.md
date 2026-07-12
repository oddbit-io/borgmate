<p align="center">
  <img src="BorgMate/Assets/borgmate-256.png" width="128" height="128" alt="BorgMate">
  <h1 align="center">BorgMate</h1>
  <p align="center">A cross-platform desktop GUI for <a href="https://www.borgbackup.org/">BorgBackup</a> 1.4 (and 2, experimental), built with .NET 10 and <a href="https://avaloniaui.net/">Avalonia UI</a> 12</p>
</p>

<p align="center">
  <img src="screenshots/repositories.png" width="800" alt="BorgMate — Repositories">
</p>

## Features

- Manage Borg repositories on local disks, SSH servers, and SMB shares, plus raw Borg 2 URLs such as `sftp://`, `rest://`, `rclone:`, `s3:`, and `b2:`
- Experimental support for Borg 2.0.0b21+ repositories alongside Borg 1.4.x
- Create and schedule backups with real-time progress, speed, and ETA, optional system wake, and AC-power-only runs
- Browse, compare, restore, and delete archives
- Repository maintenance (check, compact)
- Secure passphrase storage via OS keychain (macOS Keychain, libsecret, Windows Credential Manager)
- SSH key passphrase support
- Automatic retry on transient SSH errors and stale repository locks
- Operation journal with history and OS notifications
- System tray with start at login, start minimized, and background scheduling

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download) (for building)
- [BorgBackup 1.4.x](https://www.borgbackup.org/releases/) (Borg 2.0.0b21+ optional, for experimental Borg 2 repositories — requires a separate `borg2` binary on PATH or a custom path set in Settings)
- [OpenSSH](https://www.openssh.com/) (for remote repositories)
- **Windows:** [WSL](https://learn.microsoft.com/en-us/windows/wsl/install) with borg installed inside it

## Build & Run

```bash
dotnet build
dotnet run --project BorgMate
dotnet run --project BorgMate -- --demo   # fake data for screenshots (debug only)
dotnet test                               # run unit tests
```

## Project Structure

```
BorgMate/             App project (Avalonia)
BorgMate.Tests/       xUnit tests with NSubstitute
BorgMate.slnx         Solution file
```

## Supported Platforms

| Platform       | Architecture |
|----------------|-------------|
| macOS          | x64, arm64  |
| Linux          | x64, arm64  |
| Windows (WSL)  | x64, arm64  |

## Versioning

BorgMate release numbers follow [Semantic Versioning 2.0.0](https://semver.org/).

Before 1.0.0, BorgMate is under initial development: minor releases (0.9 → 0.10)
may include incompatible changes, patch releases contain only backward-compatible
fixes. Pre-release builds carry a suffix such as `0.10.0-beta.1` and are older
than the final release with the same number.

Released versions are immutable; every change is published as a new version.
The release version comes from an exact SemVer 2.0.0 Git tag prefixed with `v`,
for example `v1.0.0-rc.1+build.5`. Other commits append `dev`, the short commit
ID, and optional `dirty` identifiers to the build metadata of the latest tag.
Stable and pre-release packages use the same update channel.

Platforms that require numeric versions receive the `MAJOR.MINOR.PATCH` core.
The macOS bundle build number is the release commit count; the full SemVer
remains available in the application UI and assembly metadata.

## License

Licensed under the [GNU General Public License v3.0](COPYING). A commercial license is available for proprietary use cases — contact contact@oddbit.io for details.
