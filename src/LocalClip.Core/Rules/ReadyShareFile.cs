using System;
using System.IO;
using LocalClip.Core.Models;

namespace LocalClip.Core.Rules;

/// <summary>
/// Last-mile safe drag/copy guard for an export previously validated by
/// ClipExporter. This is NOT an independent codec/decode certification.
/// </summary>
public static class ReadyShareFile
{
    public static bool TryGet(ClipMetadata? selected, int targetMb, out string path)
    {
        path = "";
        if (selected is null || !selected.IsExportReady ||
            !ShareExportTargets.IsValid(targetMb) ||
            string.IsNullOrWhiteSpace(selected.ExportedFilePath) ||
            !selected.ExportedFilePath.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
            return false;

        try
        {
            // A legacy same-name export may have been overwritten by a
            // different linked clip. Keep it on disk but require re-export.
            if (!string.Equals(Path.GetFileName(selected.ExportedFilePath),
                ShareExportNaming.ForOriginal(selected.OriginalFilePath, targetMb),
                StringComparison.OrdinalIgnoreCase)) return false;

            string fingerprint = ExportFingerprint.Compute(
                selected.OriginalFilePath, selected.TrimStartSeconds,
                selected.TrimEndSeconds > 0
                    ? selected.TrimEndSeconds : selected.DurationSeconds,
                selected.AudioTracks, targetMb);

            if (!string.Equals(fingerprint, selected.LastExportFingerprint,
                StringComparison.Ordinal)) return false;

            var exported = new FileInfo(selected.ExportedFilePath);
            if (!exported.Exists || exported.Length == 0 ||
                exported.Length > ShareExportTargets.ToBytes(targetMb) ||
                exported.Length != selected.ExportedFileSizeBytes) return false;
            path = exported.FullName;
            return true;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
