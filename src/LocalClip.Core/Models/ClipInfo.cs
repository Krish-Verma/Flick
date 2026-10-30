using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Text.Json.Serialization;

namespace LocalClip.Core.Models;

public class AudioTrackInfo : INotifyPropertyChanged
{
    private bool _isMuted;
    private double _gain = 1.0;

    public event PropertyChangedEventHandler? PropertyChanged;

    public int StreamIndex { get; set; }
    public int TrackIndex { get; set; }
    public string Name { get; set; } = "";
    public string Codec { get; set; } = "";
    public LocalClip.Core.Audio.AudioTrackRole Role { get; set; }

    public bool IsMuted
    {
        get => _isMuted;
        set
        {
            if (_isMuted == value) return;
            _isMuted = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsMuted)));
        }
    }

    public double Gain
    {
        get => _gain;
        set
        {
            double safe = double.IsFinite(value) ? Math.Clamp(value, 0.0, 2.0) : 1.0;
            if (Math.Abs(_gain - safe) < 0.000001) return;
            _gain = safe;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Gain)));
        }
    }
}

public class ClipMetadata : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    private string _thumbnailPath = "";
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    private string _originalFilePath = "";
    public string OriginalFilePath
    {
        get => _originalFilePath;
        set
        {
            if (_originalFilePath == value) return;
            _originalFilePath = value;
            _available = null;
            foreach (string name in new[] { nameof(OriginalFilePath), nameof(IsAvailable), nameof(HasKnownAvailability),
                         nameof(IsAvailableForUi), nameof(LibraryStatus), nameof(LibraryStatusForUi), nameof(DisplayTitle) })
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
    private string _title = "";
    public string Title
    {
        get => _title;
        set
        {
            if (_title == value) return;
            _title = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Title)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayTitle)));
        }
    }
    [JsonIgnore] public string DisplayTitle => LocalClip.Core.Rules.ClipTitleRules.ForDisplay(
        Title, OriginalFilePath, TargetName, CreatedAt, IsImported);
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public double DurationSeconds { get; set; }
    public long FileSizeBytes { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int Fps { get; set; }
    public string TargetName { get; set; } = "";
    public bool IsFavorite { get; set; }
    public List<string> Tags { get; set; } = new();
    public string GameName { get; set; } = "";
    public List<ClipMarker> Markers { get; set; } = new();
    public string ExportAspectPreset { get; set; } = "Original";
    public DateTimeOffset? LastExportedAtUtc { get; set; }
    public int LastExportTargetMb { get; set; }
    [JsonIgnore] public string GameGroup => string.IsNullOrWhiteSpace(GameName)
        ? (string.IsNullOrWhiteSpace(TargetName) ? "Ungrouped" : TargetName) : GameName;
    [JsonIgnore] public string TagsText => string.Join(", ", Tags);
    [JsonIgnore] public string OrganizationSummary => (IsFavorite ? "Favorite · " : "") + GameGroup;
    private bool _showGameHeading;
    [JsonIgnore] public bool ShowGameHeading
    {
        get => _showGameHeading;
        set { if (_showGameHeading == value) return; _showGameHeading = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowGameHeading))); }
    }

    // Source ownership is explicit: linked/imported media is NEVER deleted by
    // a normal Remove From Library operation.
    public string SourceKind { get; set; } = "Flick";
    public string ConnectedFolderId { get; set; } = "";
    public string SourceLabel { get; set; } = "";

    [JsonIgnore]
    public bool IsImported =>
        string.Equals(SourceKind, "Imported", StringComparison.OrdinalIgnoreCase) ||
        !string.IsNullOrWhiteSpace(ConnectedFolderId);

    [JsonIgnore]
    public bool IsAvailable => _available ?? File.Exists(OriginalFilePath);
    private bool? _available;

    // WPF uses only these observed values. The general IsAvailable contract
    // retains its fresh fallback for Core/non-UI callers.
    [JsonIgnore] public bool HasKnownAvailability => _available.HasValue;
    [JsonIgnore] public bool IsAvailableForUi => _available == true;
    [JsonIgnore] public string LibraryStatusForUi => !_available.HasValue ? "Checking…" :
        _available.Value ? (IsImported ? "Imported" : "Flick") : "Offline";

    /// <summary>Apply on the UI dispatcher; a replaced path must not inherit an old read's result.</summary>
    public bool ApplyAvailabilityObservation(string originalPath, bool available)
    {
        if (!string.Equals(originalPath, OriginalFilePath, StringComparison.Ordinal) || _available == available) return false;
        _available = available;
        foreach (string name in new[] { nameof(IsAvailable), nameof(HasKnownAvailability), nameof(IsAvailableForUi),
                     nameof(LibraryStatus), nameof(LibraryStatusForUi) })
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }

    /// <summary>Refresh at library discovery/rescan, never for every search keystroke.</summary>
    public void RefreshAvailability()
    {
        ApplyAvailabilityObservation(OriginalFilePath, File.Exists(OriginalFilePath));
    }

    public void NotifyOrganizationChanged()
    {
        foreach (string property in new[] { nameof(IsFavorite), nameof(Tags), nameof(TagsText), nameof(GameName), nameof(GameGroup), nameof(OrganizationSummary), nameof(Markers), nameof(ExportAspectPreset) })
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    }

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

public sealed record ClipMarker(double PositionSeconds, string Label)
{
    [JsonIgnore] public string DisplayText => LocalClip.Core.Rules.TrimTimelineMath.Format(PositionSeconds) +
        (string.IsNullOrWhiteSpace(Label) ? "" : " · " + Label);
}

public sealed record RecentClipExport(string ClipId, string Title, string FilePath, long Bytes,
    DateTimeOffset CreatedAtUtc, string AspectPreset, int TargetMb)
{
    public string Detail => $"{Bytes / 1_000_000d:F2} MB · {LocalClip.Core.Rules.ExportAspectPresets.Label(AspectPreset)}";
}
