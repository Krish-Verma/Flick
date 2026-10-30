using System.IO;
using System.Windows.Media.Imaging;

namespace LocalClip.App.Services;

/// <summary>Small local stills only. File inspection and decoding always run away from the dispatcher.</summary>
public sealed class BoundedThumbnailLoader
{
    public const int MaximumEdge = 256;
    public const int MaximumPendingRequests = 64;
    private const int MaximumEncodedBytes = 8 * 1024 * 1024;
    private const long MaximumSourcePixels = 16L * 1024 * 1024;
    private readonly SemaphoreSlim _workers;
    private readonly int _maximumCacheEntries;
    private readonly object _cacheGate = new();
    private readonly Dictionary<string, CacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);
    private long _access;
    private int _pending;
    private sealed record CacheEntry(long Length, long ModifiedTicks, BitmapSource Image, long Access);

    public BoundedThumbnailLoader(int maximumCacheEntries = 32, int maximumConcurrency = 2)
    {
        if (maximumCacheEntries is < 0 or > 32) throw new ArgumentOutOfRangeException(nameof(maximumCacheEntries));
        if (maximumConcurrency is < 1 or > 2) throw new ArgumentOutOfRangeException(nameof(maximumConcurrency));
        _maximumCacheEntries = maximumCacheEntries;
        _workers = new SemaphoreSlim(maximumConcurrency, maximumConcurrency);
    }

    public int CacheEntryCount { get { lock (_cacheGate) return _cache.Count; } }
    public void ClearCache() { lock (_cacheGate) _cache.Clear(); }

    public async Task<BitmapSource?> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Syntax checks perform no IO; never probe a network thumbnail merely to decorate a row.
        if (!SharePreviewLoader.TryGetLocalPath(path, out string local)) return null;
        if (Interlocked.Increment(ref _pending) > MaximumPendingRequests)
        {
            Interlocked.Decrement(ref _pending);
            return null;
        }
        bool acquired = false;
        try
        {
            await _workers.WaitAsync(cancellationToken).ConfigureAwait(false);
            acquired = true;
            return await Task.Run(() => Decode(local, cancellationToken), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (acquired) _workers.Release();
            Interlocked.Decrement(ref _pending);
        }
    }

    private BitmapSource? Decode(string path, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var info = new FileInfo(path);
            if (!info.Exists || info.Length is <= 0 or > MaximumEncodedBytes) return null;
            long length = info.Length, modified = info.LastWriteTimeUtc.Ticks;
            lock (_cacheGate)
            {
                if (_cache.TryGetValue(path, out var cached) && cached.Length == length && cached.ModifiedTicks == modified)
                {
                    _cache[path] = cached with { Access = ++_access };
                    return cached.Image;
                }
                _cache.Remove(path);
            }
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var bytes = new MemoryStream((int)length);
            byte[] chunk = new byte[16384];
            int read;
            while ((read = file.Read(chunk, 0, chunk.Length)) != 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (bytes.Length + read > MaximumEncodedBytes) return null;
                bytes.Write(chunk, 0, read);
            }
            cancellationToken.ThrowIfCancellationRequested();
            bytes.Position = 0;
            var metadata = BitmapDecoder.Create(bytes, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            var frame = metadata.Frames[0];
            if (frame.PixelWidth <= 0 || frame.PixelHeight <= 0 ||
                (long)frame.PixelWidth * frame.PixelHeight > MaximumSourcePixels) return null;
            bytes.Position = 0;
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = bytes;
            if (frame.PixelWidth >= frame.PixelHeight) image.DecodePixelWidth = Math.Min(MaximumEdge, frame.PixelWidth);
            else image.DecodePixelHeight = Math.Min(MaximumEdge, frame.PixelHeight);
            image.EndInit();
            // BitmapImage.StreamSource retains its stream dependency even after
            // OnLoad closes decoding. Copy only the bounded BGRA pixels so an
            // LRU entry cannot retain the up-to-8MiB encoded MemoryStream buffer.
            var converted = new FormatConvertedBitmap(image, System.Windows.Media.PixelFormats.Bgra32, null, 0);
            int stride = checked(converted.PixelWidth * 4);
            var pixels = new byte[checked(stride * converted.PixelHeight)];
            converted.CopyPixels(pixels, stride, 0);
            var result = BitmapSource.Create(converted.PixelWidth, converted.PixelHeight, 96, 96,
                System.Windows.Media.PixelFormats.Bgra32, null, pixels, stride);
            result.Freeze();
            cancellationToken.ThrowIfCancellationRequested();
            // Do not retain a result read across an overwrite or deletion.
            info.Refresh();
            if (!info.Exists || info.Length != length || info.LastWriteTimeUtc.Ticks != modified) return null;
            lock (_cacheGate)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_maximumCacheEntries > 0)
                {
                    _cache[path] = new(length, modified, result, ++_access);
                    while (_cache.Count > _maximumCacheEntries)
                        _cache.Remove(_cache.MinBy(item => item.Value.Access).Key);
                }
            }
            return result;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException
            or FormatException or NotSupportedException or InvalidOperationException
            or System.Runtime.InteropServices.COMException)
        { return null; }
    }
}
