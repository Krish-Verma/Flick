using System;
using System.IO;
using LocalClip.Core.Rules;

namespace LocalClip.App.Services;

public static class NotificationSoundFile
{
    // Called only after a local picker/explicit preview or confirmed save;
    // never during startup, disabled notifications or clip persistence.
    public static byte[] Read(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) ||
            path.StartsWith(@"\\", StringComparison.Ordinal) ||
            !string.Equals(Path.GetExtension(path), ".wav", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Choose a local WAV file.");
        string full = Path.GetFullPath(path);
        if (new DriveInfo(Path.GetPathRoot(full)!).DriveType == DriveType.Network ||
            (File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Choose a local file, not a network path or link.");
        using var file = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length < 44 || file.Length > NotificationWaveRules.MaximumBytes)
            throw new InvalidDataException("Sound must be at most 1 MiB and 3 seconds.");
        var bytes = new byte[(int)file.Length];
        file.ReadExactly(bytes);
        if (!NotificationWaveRules.IsValid(bytes))
            throw new InvalidDataException("Use a valid PCM WAV: mono/stereo, 8/16-bit, 8–48 kHz, at most 3 seconds.");
        return bytes;
    }
}
