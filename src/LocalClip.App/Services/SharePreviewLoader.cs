using System.IO;
using System.Windows.Media.Imaging;

namespace LocalClip.App.Services;

/// <summary>One bounded still image, never another video decoder or a remote URI.</summary>
internal static class SharePreviewLoader
{
    internal const long MaximumSourcePixels = 16L * 1024 * 1024;

    internal static bool TryGetLocalPath(string path, out string normalized)
    {
        normalized = "";
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) return false;
        try
        {
            // Windows accepts forward slashes in UNC paths. Normalize before
            // deciding whether a thumbnail path belongs to a local root.
            string fullPath = Path.GetFullPath(path);
            if (fullPath.StartsWith(@"\\", StringComparison.Ordinal)) return false;
            normalized = fullPath;
            return true;
        }
        catch (Exception error) when (error is IOException or ArgumentException or NotSupportedException)
        { return false; }
    }

    public static async Task<BitmapSource?> LoadAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryGetLocalPath(path, out string normalized)) return null;
        try
        {
            await using var file = new FileStream(normalized, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 16384, FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (file.Length is <= 0 or > 8 * 1024 * 1024) return null;
            using var bytes = new MemoryStream((int)file.Length);
            byte[] chunk = new byte[16384];
            int count;
            while ((count = await file.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) != 0)
            {
                if (bytes.Length + count > 8 * 1024 * 1024) return null;
                bytes.Write(chunk, 0, count);
            }
            cancellationToken.ThrowIfCancellationRequested();
            bytes.Position = 0;
            var metadata = BitmapDecoder.Create(bytes, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            var frame = metadata.Frames[0];
            if (frame.PixelWidth is <= 0 or > 32768 || frame.PixelHeight is <= 0 or > 32768 ||
                (long)frame.PixelWidth * frame.PixelHeight > MaximumSourcePixels) return null;
            bool landscape = frame.PixelWidth >= frame.PixelHeight;
            bytes.Position = 0;
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = bytes;
            if (landscape) image.DecodePixelWidth = Math.Min(480, frame.PixelWidth);
            else image.DecodePixelHeight = Math.Min(480, frame.PixelHeight);
            image.EndInit();
            cancellationToken.ThrowIfCancellationRequested();
            image.Freeze();
            return image;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException
            or ArgumentException or FormatException or NotSupportedException or InvalidOperationException
            or System.Runtime.InteropServices.COMException)
        { return null; }
    }
}
