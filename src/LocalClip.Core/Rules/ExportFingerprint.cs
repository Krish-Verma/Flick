using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using LocalClip.Core.Models;

namespace LocalClip.Core.Rules;

public static class ExportFingerprint
{
    public const int PROFILE_VERSION = 1;

    public static string Compute(
        string sourceFilePath,
        double startSec,
        double endSec,
        IEnumerable<AudioTrackInfo> tracks,
        int targetSizeMb)
    {
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
        sb.Append($"START:{startSec:F3}|END:{endSec:F3}|TARGET_MB:{targetSizeMb}|V:{PROFILE_VERSION}|");

        if (tracks != null)
        {
            foreach (var t in tracks)
            {
                sb.Append($"T:{t.StreamIndex}:{t.IsMuted}:{t.Gain:F2}|");
            }
        }

        byte[] bytes = Encoding.UTF8.GetBytes(sb.ToString());
        byte[] hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
