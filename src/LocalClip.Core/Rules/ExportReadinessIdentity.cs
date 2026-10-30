using System.Text.Json;
using LocalClip.Core.Models;

namespace LocalClip.Core.Rules;

/// <summary>Only state affecting a prepared file's validity. No filesystem reads or decorative metadata.</summary>
public static class ExportReadinessIdentity
{
    public static string Capture(ClipMetadata clip) => JsonSerializer.Serialize(new
    {
        clip.Id, clip.OriginalFilePath, clip.DurationSeconds, clip.TrimStartSeconds, clip.TrimEndSeconds,
        clip.ExportAspectPreset, clip.ExportedFilePath, clip.ExportedFileSizeBytes, clip.IsExportReady,
        clip.LastExportFingerprint, clip.LastExportTargetMb, clip.LastExportedAtUtc,
        Tracks = clip.AudioTracks.Select(track => new { track.StreamIndex, track.TrackIndex, track.Role, track.IsMuted, track.Gain }).ToArray()
    });
}
