using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using LocalClip.Core.Models;

namespace LocalClip.Core.Rules;

public static class ExportFingerprint
{
    public const int PROFILE_VERSION = 3;

    public static string Compute(
        string sourceFilePath,
        double startSec,
        double endSec,
        IEnumerable<AudioTrackInfo> tracks,
        int targetSizeMb,
        string aspectPreset = ExportAspectPresets.Original)
    {
        if (!ExportAspectPresets.IsValid(aspectPreset)) throw new ArgumentException("Unsupported export aspect preset.", nameof(aspectPreset));
        long lastWriteTicks = 0;
        long sourceLength = 0;
        try
        {
            if (File.Exists(sourceFilePath))
            {
                var fi = new FileInfo(sourceFilePath);
                lastWriteTicks = fi.LastWriteTimeUtc.Ticks;
                sourceLength = fi.Length;
            }
        }
        catch { }

        var sb = new StringBuilder();
        sb.Append($"PATH:{sourceFilePath}|TICKS:{lastWriteTicks}|LEN:{sourceLength}|");
        sb.Append($"START:{startSec.ToString("R", CultureInfo.InvariantCulture)}|END:{endSec.ToString("R", CultureInfo.InvariantCulture)}|TARGET_MB:{targetSizeMb}|ASPECT:{aspectPreset}|V:{PROFILE_VERSION}|");

        if (tracks != null)
        {
            foreach (var t in tracks)
            {
                sb.Append($"T:{t.StreamIndex}:{t.IsMuted}:{t.Gain.ToString("R", CultureInfo.InvariantCulture)}|");
            }
        }

        byte[] bytes = Encoding.UTF8.GetBytes(sb.ToString());
        byte[] hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
