using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Velopack;
using Velopack.Sources;

namespace BorgMate.Infrastructure.Updates;

public class UpdateService
{
    private const string UpdateBaseUrl = "https://borgmate.oddbit.io/releases";

    private readonly ILogger<UpdateService> _logger;
    private readonly VerifiedUpdateManager _manager;
    private UpdateInfo? _update;

    public UpdateService(ILogger<UpdateService> logger, ReleaseSignatureVerifier verifier)
    {
        _logger = logger;
        _manager = new VerifiedUpdateManager(new SignedReleaseFeedSource(
            new Uri($"{UpdateBaseUrl}/{ReleasePlatform}"), ReleasePlatform, new HttpClientFileDownloader(), verifier));
    }

    private static string ReleasePlatform
    {
        get
        {
            var os = OperatingSystem.IsMacOS() ? "macos"
                : OperatingSystem.IsWindows() ? "win"
                : "linux";
            var arch = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "arm64" : "x64";
            return $"{os}-{arch}";
        }
    }

    public bool IsUpdateAvailable => _update is not null;
    public string? AvailableVersion => _update?.TargetFullRelease.Version.ToFullString();

    public async Task CheckForUpdatesAsync()
    {
        try
        {
            if (!_manager.IsInstalled)
            {
                _logger.LogDebug("App is not installed via Velopack, skipping update check");
                return;
            }

            _logger.LogInformation("Checking for updates for {Platform}", ReleasePlatform);

            _update = await _manager.CheckForUpdatesAsync();
            if (_update is not null)
                _logger.LogInformation("Update available: {Version}", AvailableVersion);
            else
                _logger.LogInformation("No updates available");
        }
        catch (ReleaseVerificationException ex)
        {
            _logger.LogWarning(ex, "Update check rejected the release: {Failure}", ex.Failure);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Update check failed");
        }
    }

    public async Task<bool> DownloadUpdateAsync(Action<int>? progress = null)
    {
        var update = _update ?? throw new InvalidOperationException("No update is available to download.");
        try
        {
            _logger.LogInformation("Downloading update {Version} (file: {FileName})",
                update.TargetFullRelease.Version, update.TargetFullRelease.FileName);
            await _manager.DownloadUpdatesAsync(update, progress);
            _logger.LogInformation("Update downloaded successfully");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download update");
            return false;
        }
    }

    /// <summary>
    ///     Fetches changelog entries between the current version and the target version.
    /// </summary>
    public async Task<List<ChangelogEntry>> FetchChangelogAsync(string currentVersion, string targetVersion)
    {
        var entries = new List<ChangelogEntry>();
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var json = await http.GetStringAsync("https://borgmate.oddbit.io/changelog-v2.json");
            entries.AddRange(ParseChangelog(json, currentVersion, targetVersion));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch changelog");
        }

        return entries;
    }

    internal static List<ChangelogEntry> ParseChangelog(string json, string currentVersion, string targetVersion)
    {
        var current = SemVerParser.Parse(currentVersion);
        var target = SemVerParser.Parse(targetVersion);
        var changelog = JsonSerializer.Deserialize<List<ChangelogEntry>>(json, JsonSerializerOptions.Web)
                        ?? throw new JsonException("Changelog is null.");
        var entries = new List<ChangelogEntry>();

        foreach (var entry in changelog)
        {
            if (string.IsNullOrWhiteSpace(entry.Version))
                throw new JsonException("Changelog version is empty.");
            if (entry.Changes is null)
                throw new JsonException($"Changelog changes are null for version {entry.Version}.");

            var version = SemVerParser.Parse(entry.Version);
            if (version <= current || version > target) continue;

            entries.Add(entry);
        }

        return entries;
    }

    /// <summary>Applies the downloaded update and restarts the application.</summary>
    public async Task ApplyAndRestartAsync()
    {
        var update = _update ?? throw new InvalidOperationException("No update is available to apply.");
        try
        {
            await _manager.VerifyReleaseAsync(update.TargetFullRelease);
            _logger.LogInformation("Applying update and restarting...");
            _manager.ApplyUpdatesAndRestart(update);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply update");
        }
    }
}
