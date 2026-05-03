using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Velopack;

namespace BorgMate.Services;

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
        if (Environment.GetEnvironmentVariable("FLATPAK_ID") is not null)
        {
            logger.LogInformation("Skipping update check: managed by Flatpak host");
            return;
        }

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
                AvailableVersion = update.TargetFullRelease.Version.ToString();
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
    /// Fetches changelog entries between the current version and the target version.
    /// </summary>
    public async Task<List<ChangelogEntry>> FetchChangelogAsync(string currentVersion, string targetVersion)
    {
        var entries = new List<ChangelogEntry>();
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var json = await http.GetStringAsync("https://borgmate.oddbit.io/changelog.json");
            using var doc = JsonDocument.Parse(json);

            var current = Version.TryParse(currentVersion, out var cv) ? cv : null;
            var target = Version.TryParse(targetVersion, out var tv) ? tv : null;
            if (current is null || target is null) return entries;

            foreach (var item in doc.RootElement.EnumerateArray())
            {
                var ver = item.GetProperty("version").GetString();
                if (ver is null || !Version.TryParse(ver, out var v)) continue;
                if (v <= current || v > target) continue;

                var date = item.TryGetProperty("date", out var d) ? d.GetString() : null;
                var changes = new List<string>();
                if (item.TryGetProperty("changes", out var arr))
                    foreach (var c in arr.EnumerateArray())
                        if (c.GetString() is { } s) changes.Add(s);

                entries.Add(new ChangelogEntry(ver, date, changes));
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch changelog");
        }
        return entries;
    }

    public record ChangelogEntry(string Version, string? Date, List<string> Changes);

    public void ApplyAndRestart()
    {
        try
        {
            var mgr = new UpdateManager(UpdateUrl);
            logger.LogInformation("Applying update and restarting...");
            var update = mgr.CheckForUpdatesAsync().GetAwaiter().GetResult();
            if (update is null) return;
            mgr.ApplyUpdatesAndRestart(update);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to apply update");
        }
    }
}
