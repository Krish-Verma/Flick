using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LocalClip.Core.Models;

public class AppSettings
{
    public int SchemaVersion { get; set; } = 1;

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
    public string VideoEncoder { get; set; } = "obs_nvenc_h264_tex"; // Fallbacks: "jim_nvenc", "obs_x264"
    public int ReplayDurationSeconds { get; set; } = 30;
    public int BufferMemoryLimitMb { get; set; } = 256;

    // Audio Profile
    public string AudioMode { get; set; } = "SystemFallback"; // "Separated" or "SystemFallback"
    public bool EnableMicrophone { get; set; } = true;
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
        try
        {
            if (File.Exists(path))
            {
                string json = File.ReadAllText(path);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json);
                if (loaded != null) return loaded;
            }
        }
        catch { }
        var def = CreateDefault();
        def.Save();
        return def;
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
