<p align="center">
  <img src="BorgMate/Assets/borgmate-256.png" width="128" height="128" alt="BorgMate">
  <h1 align="center">BorgMate</h1>
  <p align="center">A cross-platform desktop GUI for <a href="https://www.borgbackup.org/">BorgBackup</a> 1.4, built with .NET 10 and <a href="https://avaloniaui.net/">Avalonia UI</a> 12</p>
</p>

## Security warning

Attackers are copying legitimate projects on GitHub and distributing malware disguised as ready-to-use binaries. They are also using BorgMate’s source code to do this.

Check the website address before downloading. Download official BorgMate binaries only from [borgmate.oddbit.io](https://borgmate.oddbit.io) or our official GitHub repository: [github.com/oddbit-io/borgmate](https://github.com/oddbit-io/borgmate).

Official BorgMate download packages are signed with GPG. These signatures cover only the packages; the installed binaries themselves are not signed. The signatures are available in our [GitHub releases](https://github.com/oddbit-io/borgmate/releases). GPG signing key fingerprint: `2D0B01C4BDAAF32BA376AE1E793239019CE4CF26`.

<p align="center">
  <img src="screenshots/repositories.png" width="800" alt="BorgMate — Repositories">
</p>

## Features

- Manage Borg repositories on local disks, SSH servers, and SMB shares, plus raw Borg 2 URLs such as `sftp://`, `rest://`, `rclone:`, `s3:`, and `b2:`
- Experimental support for Borg 2.0.0b21 repositories alongside Borg 1.4.x
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
- [BorgBackup 1.4.x](https://www.borgbackup.org/releases/), 1.4.5 or newer recommended (only Borg 2.0.0b21 is supported for experimental Borg 2 repositories — requires a separate `borg2` binary on PATH or a custom path set in Settings)
- [OpenSSH](https://www.openssh.com/) (for remote repositories)
- **Windows:** [WSL](https://learn.microsoft.com/en-us/windows/wsl/install) with borg installed inside it

## Supported Platforms

| Platform       | Architecture |
|----------------|-------------|
| macOS          | x64, arm64  |
| Linux          | x64, arm64  |
| Windows (WSL)  | x64, arm64  |

## Versioning

BorgMate release numbers follow [Semantic Versioning 2.0.0](https://semver.org/).

## License

Licensed under the [GNU General Public License v3.0](COPYING). A commercial license is available for proprietary use cases — contact contact@oddbit.io for details.
