using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Text.Json.Serialization;

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

public class ClipMetadata : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    private string _thumbnailPath = "";
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

    // Source ownership is explicit: linked/imported media is NEVER deleted by
    // a normal Remove From Library operation.
    public string SourceKind { get; set; } = "Flick";
    public string ConnectedFolderId { get; set; } = "";
    public string SourceLabel { get; set; } = "";

    [JsonIgnore]
    public bool IsImported => SourceKind == "Imported";

    [JsonIgnore]
    public bool IsAvailable => File.Exists(OriginalFilePath);

    [JsonIgnore]
    public string LibraryStatus => IsAvailable ? (IsImported ? "Imported" : "Flick") : "Offline";

    [JsonIgnore]
    public string LibrarySource => IsImported ? (string.IsNullOrWhiteSpace(SourceLabel) ? "Imported Clip" : SourceLabel) : "Flick";

    [JsonIgnore]
    public string LibraryDeleteAction => IsImported ? "Remove" : "Delete";

    public string ThumbnailPath
    {
        get => _thumbnailPath;
        set
        {
            if (_thumbnailPath == value) return;
            _thumbnailPath = value;
            PropertyChanged?.Invoke(this,
                new PropertyChangedEventArgs(nameof(ThumbnailPath)));
        }
    }

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
