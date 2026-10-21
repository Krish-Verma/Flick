using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LocalClip.Core.Models;

public class AppSettings
{
    public int SchemaVersion { get; set; } = 2;

    // Capture Target
    public string TargetType { get; set; } = "Monitor"; // "Monitor" or "Window"
    public int MonitorIndex { get; set; } = 0;
    public ulong WindowHwnd { get; set; } = 0;
    public string WindowTitle { get; set; } = "";
    public string WindowExecutable { get; set; } = "";

    // Video Profile
    public int VideoWidth { get; set; } = 1920;
    public int VideoHeight { get; set; } = 1080;
    public int VideoFps { get; set; } = 30;
    public int VideoBitrateKbps { get; set; } = 15000;
    public string VideoEncoder { get; set; } = "obs_nvenc_h264_tex"; // NVENC hardware only, jim_nvenc fallback; no silent x264 capture
    public int ReplayDurationSeconds { get; set; } = 30;
    public int BufferMemoryLimitMb { get; set; } = 256;
    public string QualityPreset { get; set; } = "Balanced"; // Light, Balanced, Smooth; user-visible presets

    // Audio Profile
    public string AudioMode { get; set; } = "SystemFallback"; // "Separated" or "SystemFallback"
    public bool EnableMicrophone { get; set; } = false; // Privacy: opt-in, never access mic on first run
    public string MicrophoneDeviceId { get; set; } = "default";
    public string SystemAudioDeviceId { get; set; } = "default";
    public int GameProcessId { get; set; } = 0;
    public int DiscordProcessId { get; set; } = 0;

    // Hotkeys (Configurable)
    public string CaptureToggleHotkey { get; set; } = "Ctrl+Shift+F9";
    public string SaveReplayHotkey { get; set; } = "Ctrl+Shift+F8";

    // Storage Paths
    public string StorageRoot { get; set; } = "";
    public string OriginalsFolder { get; set; } = "";
    public string ExportsFolder { get; set; } = "";
    public string TempFolder { get; set; } = "";

    // Discord Export Presets (MB = 1,000,000 bytes)
    public int DiscordTargetSizeMb { get; set; } = 20; // 20 MB free default
    public bool UseTwoPassSoftwareIfHardwareOvershoots { get; set; } = false;

    // Direct Webhook (Optional, secret stored in DPAPI)
    public bool EnableDiscordWebhook { get; set; } = false;
    public string DiscordWebhookChannelName { get; set; } = "";
    public string MaskedWebhookUrl { get; set; } = "";

    // App Behavior
    public bool StartWithWindows { get; set; } = false;
    public bool StartMinimizedToTray { get; set; } = false;

    // Clip feedback is independently opt-out. No overlay or sound engine is
    // resident until a confirmed saved clip or a user-initiated preview.
    public bool ShowClipSavedOverlay { get; set; } = true;
    public bool PlayClipSavedSound { get; set; } = true;
    // Nonpersistent diagnostic: unreadable/invalid settings are not deleted or
    // silently overwritten. Display the warning after app initialization.
    [JsonIgnore]
    public string LoadWarning { get; private set; } = "";

    // ALWAYS FALSE by specification: "Capture starts DISARMED"
    [JsonIgnore]
    public bool StartArmed => false;

    public static AppSettings CreateDefault()
    {
        var settings = new AppSettings();
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string myVideos = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
        if (string.IsNullOrWhiteSpace(myVideos))
        {
            myVideos = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Videos");
        }

        settings.StorageRoot = Path.Combine(myVideos, "LocalClip");
        settings.OriginalsFolder = Path.Combine(settings.StorageRoot, "Originals");
        settings.ExportsFolder = Path.Combine(settings.StorageRoot, "Exports");
        settings.TempFolder = Path.Combine(localAppData, "LocalClip", "Temp");
        return settings;
    }

    public static string GetSettingsFilePath()
    {
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LocalClip");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "settings.json");
    }

    public static AppSettings Load()
    {
        string path = GetSettingsFilePath();
        if (File.Exists(path))
            return LoadExistingFile(path);

        var initial = CreateDefault();
        initial.Save();
        return initial;
    }

    /// <summary>
    /// Isolated settings recovery. Valid old files are migrated in memory;
    /// malformed originals are never overwritten. Future explicit Save is
    /// the only way to replace a damaged JSON file.
    /// </summary>
    public static AppSettings LoadExistingFile(string filePath)
    {
        try
        {
            var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(filePath))
                ?? throw new JsonException("Settings JSON contains no configuration.");
            var defaults = CreateDefault();
            bool repaired = false;
            if (loaded.SchemaVersion < 2)
            {
                loaded.EnableMicrophone = false;
                loaded.SchemaVersion = 2;
                repaired = true;
            }
            if (loaded.ReplayDurationSeconds is < 15 or > 120)
            {
                loaded.ReplayDurationSeconds = defaults.ReplayDurationSeconds;
                repaired = true;
            }
            if (loaded.DiscordTargetSizeMb is < 1 or > 250)
            {
                loaded.DiscordTargetSizeMb = defaults.DiscordTargetSizeMb;
                repaired = true;
            }
            if (loaded.VideoWidth < 1 || loaded.VideoHeight < 1 ||
                loaded.VideoFps < 1 || loaded.VideoBitrateKbps < 1)
            {
                loaded.VideoWidth = defaults.VideoWidth;
                loaded.VideoHeight = defaults.VideoHeight;
                loaded.VideoFps = defaults.VideoFps;
                loaded.VideoBitrateKbps = defaults.VideoBitrateKbps;
                loaded.VideoEncoder = defaults.VideoEncoder;
                loaded.QualityPreset = defaults.QualityPreset;
                repaired = true;
            }
            if (string.IsNullOrWhiteSpace(loaded.OriginalsFolder) ||
                !Path.IsPathFullyQualified(loaded.OriginalsFolder))
            {
                loaded.OriginalsFolder = defaults.OriginalsFolder;
                repaired = true;
            }
            if (string.IsNullOrWhiteSpace(loaded.ExportsFolder) ||
                !Path.IsPathFullyQualified(loaded.ExportsFolder))
            {
                loaded.ExportsFolder = defaults.ExportsFolder;
                repaired = true;
            }

            if (repaired)
                loaded.LoadWarning = "Some old or invalid preferences were safely reset in memory. Review Settings and Save to keep the changes.";
            return loaded;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or JsonException or ArgumentException or NotSupportedException)
        {
            var fallback = CreateDefault();
            fallback.LoadWarning =
                "Saved settings could not be read. Flick is using safe defaults for this session; the original settings.json was NOT overwritten. Back it up before saving new settings.";
            return fallback;
        }
    }

    public void Save()
    {
        string path = GetSettingsFilePath();
        string dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);
        string tempPath = path + ".tmp";
        string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, path, overwrite: true);
    }
}
