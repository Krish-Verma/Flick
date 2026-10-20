using System;
using System.Collections.Generic;
using System.IO;

namespace LocalClip.Core.Models;

public class AudioTrackInfo
{
    public int StreamIndex { get; set; }
    public int TrackIndex { get; set; }
    public string Name { get; set; } = "";
    public string Codec { get; set; } = "";
    public bool IsMuted { get; set; } = false;
    public double Gain { get; set; } = 1.0; // 0.0 to 2.0 (1.0 = unity)
}

public class ClipMetadata
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string OriginalFilePath { get; set; } = "";
    public string Title { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public double DurationSeconds { get; set; }
    public long FileSizeBytes { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int Fps { get; set; }
    public string TargetName { get; set; } = "";
    public string ThumbnailPath { get; set; } = "";

    // Multi-track audio configuration
    public List<AudioTrackInfo> AudioTracks { get; set; } = new();

    // Non-destructive Trim (in seconds)
    public double TrimStartSeconds { get; set; } = 0.0;
    public double TrimEndSeconds { get; set; } = 0.0;
    public double SelectedDurationSeconds => Math.Max(0.0, (TrimEndSeconds > 0 ? TrimEndSeconds : DurationSeconds) - TrimStartSeconds);

    // Share Export Status
    public string LastExportFingerprint { get; set; } = "";
    public string ExportedFilePath { get; set; } = "";
    public long ExportedFileSizeBytes { get; set; }
    public bool IsExportReady { get; set; } = false;

    public string FormattedDuration
    {
        get
        {
            var ts = TimeSpan.FromSeconds(DurationSeconds);
            return ts.Hours > 0 ? ts.ToString(@"h\:mm\:ss") : ts.ToString(@"m\:ss");
        }
    }

    public string FormattedSize
    {
        get
        {
            double mb = FileSizeBytes / 1_000_000.0;
            return $"{mb:F1} MB";
        }
    }
}
