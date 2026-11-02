<p align="center">
  <img src="BorgMate/Assets/borgmate-256.png" width="128" height="128" alt="BorgMate">
  <h1 align="center">BorgMate</h1>
  <p align="center">A cross-platform desktop GUI for <a href="https://www.borgbackup.org/">BorgBackup</a> 1.4, built with .NET 10 and <a href="https://avaloniaui.net/">Avalonia UI</a> 12</p>
</p>

## Security warning

Attackers are copying legitimate projects on GitHub and distributing malware disguised as ready-to-use binaries. They are also using BorgMate’s source code to do this.

Check the website address before downloading. Download official BorgMate binaries only from [borgmate.oddbit.io](https://borgmate.oddbit.io) or our official GitHub repository: [github.com/oddbit-io/borgmate](https://github.com/oddbit-io/borgmate).

Official BorgMate download packages are signed with GPG, and automatic updates install only releases whose manifest carries that signature; the installed binaries themselves are not signed. The signatures are available in our [GitHub releases](https://github.com/oddbit-io/borgmate/releases). GPG signing key fingerprint: `2D0B01C4BDAAF32BA376AE1E793239019CE4CF26`.

Automatic updates verify the final package against the signed SHA-256 before installation, including packages already cached locally.

## Security changes since 1.2.0

- **Signed update manifests.** Each platform's update feed now ships a `release-manifest.json` with a detached GPG signature. The manifest names the platform, the channel, and the feed's SHA-256. BorgMate verifies the signature against release-signing subkeys embedded in the app and offers an update only if every check passes. A tampered or foreign-signed feed, or a feed for another platform, is ignored.
- **Full update packages only.** Updates no longer use delta packages. Each update is a complete package checked against the signed SHA-256 before installation.
- **No secrets on WSL command lines.** On Windows, the Borg passphrase and the SMB password are passed into WSL through `WSLENV`. Earlier versions put them on the `wsl.exe` command line, where other processes could read them.
- **No secrets on macOS command lines.** On macOS, passphrases are now stored and read through Security.framework. Earlier versions called the `security` tool and passed the passphrase as an argument. Network share mounting uses native APIs too.
- **Relocated repositories need confirmation.** Earlier versions accepted a repository that had moved to a new location without asking. Now an interactive operation asks before accepting the new location. Scheduled runs use only relocations you have already confirmed and fail on any other.
- **Borg never waits for hidden input.** Borg now always gets an explicit passphrase and explicit answers, and its stdin is closed. It used to be able to stop at a prompt nobody could see, especially under WSL. Now it fails with a clear error. An encrypted repository is never initialized without a passphrase.

<p align="center">
  <img src="screenshots/repositories.png" width="800" alt="BorgMate — Repositories">
</p>

## Features

- Manage Borg repositories on local disks, SSH servers, and SMB shares
- Experimental Borg 2.0.0b21 repositories added in earlier versions, including raw Borg 2 URLs, keep working; new ones cannot be added
- Create and schedule backups with real-time progress, speed, and ETA, optional system wake, and AC-power-only runs
- Browse, compare, restore, and delete archives
- Repository maintenance (check, compact)
- Secure passphrase storage via OS keychain (macOS Keychain, libsecret, Windows Credential Manager)
  - On macOS, earlier versions saved passphrases through the `security` tool, so the keychain trusts only that tool for them. macOS asks once per saved passphrase before BorgMate may read it; choose **Always Allow**.
  - BorgMate binaries are not code-signed, so the keychain ties that permission to the exact binary. After an update, macOS may ask again for each saved passphrase.
  - Scheduled macOS backups fail with a credentials-required message when keychain access needs authorization. Run an operation manually to grant access before the next scheduled backup.
- SSH key passphrase support
- Automatic retry on transient SSH errors and stale repository locks
- Operation journal with history and OS notifications
- Full release history in the About window, bundled with the app
- System tray with start at login, start minimized, and background scheduling

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download) (for building)
- [BorgBackup 1.4.x](https://www.borgbackup.org/releases/), 1.4.5 or newer recommended (existing Borg 2 repositories need Borg 2.0.0b21 as a separate `borg2` binary on PATH)
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
