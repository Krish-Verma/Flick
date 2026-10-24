using System.IO;
using System.Windows;

namespace LocalClip.App.Services;

/// <summary>OS file data only. The caller must first pass ReadyShareFile's current export guard.</summary>
public static class ShareFileTransfer
{
    public static DataObject CreateFileData(string guardedPath)
    {
        if (!Path.IsPathFullyQualified(guardedPath) ||
            !Path.GetExtension(guardedPath).Equals(".mp4", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Share requires a local MP4 file.", nameof(guardedPath));
        var file = new FileInfo(guardedPath);
        if (!file.Exists || file.Length == 0) throw new IOException("The MP4 is no longer available. Export it again.");
        return new DataObject(DataFormats.FileDrop, new[] { file.FullName });
    }
}
