using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace LocalClip.Core.Rules;

/// <summary>
/// Distinct original folders may contain the same filename. Hashing the
/// canonical source path prevents one user's share export overwriting another
/// clip's same-named MP4. No private paths are sent outside this PC.
/// </summary>
public static class ShareExportNaming
{
    public static string ForOriginal(string originalFilePath, int targetMb)
    {
        if (string.IsNullOrWhiteSpace(originalFilePath))
            throw new ArgumentException("An original path is required.", nameof(originalFilePath));
        _ = ShareExportTargets.ToBytes(targetMb);
        string full = Path.GetFullPath(originalFilePath);
        string name = Path.GetFileNameWithoutExtension(full);
        if (name.Length > 70) name = name[..70];
        if (string.IsNullOrWhiteSpace(name)) name = "Clip";
        string hash = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(full.ToUpperInvariant())))[..12];
        return $"{name}_Discord_{targetMb}MB_{hash}.mp4";
    }
}
