using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Velopack;

namespace BorgMate.Infrastructure.Updates;

public class UpdateService(ILogger<UpdateService> logger)
{
    private const string UpdateBaseUrl = "https://borgmate.oddbit.io/releases";

    private static string UpdateUrl
    {
        get
        {
            var os = OperatingSystem.IsMacOS() ? "macos"
                : OperatingSystem.IsWindows() ? "win"
                : "linux";
            var arch = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "arm64" : "x64";
            return $"{UpdateBaseUrl}/{os}-{arch}";
        }
    }

    public bool IsUpdateAvailable { get; private set; }
    public string? AvailableVersion { get; private set; }

    public async Task CheckForUpdatesAsync()
    {
        try
        {
            var url = UpdateUrl;
            var mgr = new UpdateManager(url);
            if (!mgr.IsInstalled)
            {
                logger.LogDebug("App is not installed via Velopack, skipping update check");
                return;
            }

            logger.LogInformation("Checking for updates at {UpdateUrl}", url);

            var update = await mgr.CheckForUpdatesAsync();
            if (update is not null)
            {
                IsUpdateAvailable = true;
                AvailableVersion = update.TargetFullRelease.Version.ToFullString();
                logger.LogInformation("Update available: {Version}", AvailableVersion);
            }
            else
            {
                logger.LogInformation("No updates available");
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Update check failed");
        }
    }

    public async Task<bool> DownloadUpdateAsync(Action<int>? progress = null)
    {
        try
        {
            var mgr = new UpdateManager(UpdateUrl);
            var update = await mgr.CheckForUpdatesAsync();
            if (update is null) return false;

            logger.LogInformation("Downloading update {Version} from {UpdateUrl} (file: {FileName})",
                update.TargetFullRelease.Version, UpdateUrl, update.TargetFullRelease.FileName);
            await mgr.DownloadUpdatesAsync(update, progress);
            logger.LogInformation("Update downloaded successfully");
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to download update");
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
            logger.LogWarning(ex, "Failed to fetch changelog");
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

    /// <summary>Applies the available Velopack update and restarts the application.</summary>
    public async Task ApplyAndRestartAsync()
    {
        try
        {
            var mgr = new UpdateManager(UpdateUrl);
            logger.LogInformation("Applying update and restarting...");
            var update = await mgr.CheckForUpdatesAsync();
            if (update is null) return;
            mgr.ApplyUpdatesAndRestart(update);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to apply update");
        }
    }
}
