using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LocalClip.App.Services;
using Xunit;

namespace LocalClip.App.Tests;

public class AsyncThumbnailTests
{
    [Fact]
    public void LocalStillsAreFrozenBoundedCachedAndInvalidatedAfterReplacement()
    {
        InSta(() =>
        {
            using var files = new Images();
            string first = files.Write("first.png", 1200, 2400, 17);
            string second = files.Write("second.png", 1600, 900, 37);
            string third = files.Write("third.png", 40, 30, 57);
            var loader = new BoundedThumbnailLoader(maximumCacheEntries: 2);
            BitmapSource Load(string path) => Assert.IsAssignableFrom<BitmapSource>(Wait(loader.LoadAsync(path)));
            var portrait = Load(first);
            Assert.True(portrait.IsFrozen);
            Assert.InRange(portrait.PixelWidth, 1, BoundedThumbnailLoader.MaximumEdge);
            Assert.Equal(256, portrait.PixelHeight);
            Assert.Same(portrait, Load(first));
            var landscape = Load(second);
            Assert.Equal(256, landscape.PixelWidth);
            Assert.InRange(landscape.PixelHeight, 1, 256);
            Load(third);
            Assert.Equal(2, loader.CacheEntryCount);
            Assert.NotSame(portrait, Load(first)); // Oldest entry was evicted.
            files.Write("first.png", 1200, 2400, 117);
            File.SetLastWriteTimeUtc(first, DateTime.UtcNow.AddMinutes(1));
            Assert.Equal(117, Blue(Load(first)));
            loader.ClearCache();
            Assert.Equal(0, loader.CacheEntryCount);
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            Assert.ThrowsAny<OperationCanceledException>(() => Wait(loader.LoadAsync(second, cancelled.Token)));
        });
    }

    [Fact]
    public void RecycledImageAndUnloadCannotReceiveThePreviousRequestsPixels()
    {
        InSta(() =>
        {
            using var files = new Images();
            string old = files.Write("old.png", 2200, 2200, 19);
            string current = files.Write("current.png", 32, 18, 219);
            // The unshown renderer has no guaranteed installed synchronization context.
            // Result assignment must explicitly marshal to the image's dispatcher.
            SynchronizationContext.SetSynchronizationContext(null);
            var image = new Image();
            AsyncThumbnail.SetPath(image, old);
            Assert.Null(image.Source); // An unshown/hidden image performs no automatic load.
            Task oldRequest = AsyncThumbnail.LoadForSnapshotAsync(image);
            AsyncThumbnail.SetPath(image, current);
            Assert.Null(image.Source); // No recycled old poster while the new still is pending.
            Task latestRequest = AsyncThumbnail.LoadForSnapshotAsync(image);
            Wait(Task.WhenAll(oldRequest, latestRequest));
            Assert.Equal(219, Blue(Assert.IsAssignableFrom<BitmapSource>(image.Source)));

            AsyncThumbnail.SetPath(image, old);
            Task beforeUnload = AsyncThumbnail.LoadForSnapshotAsync(image);
            image.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
            Wait(beforeUnload);
            Assert.Null(image.Source);
            Assert.Equal(old, AsyncThumbnail.GetPath(image)); // Navigation preserves clip identity.
        });
    }

    [Fact]
    public void MissingCorruptRemoteAndOversizedStillsNeverRequireMediaOrHideTheRow()
    {
        InSta(() =>
        {
            using var files = new Images();
            var loader = new BoundedThumbnailLoader();
            string corrupt = Path.Combine(files.Root, "corrupt.png");
            File.WriteAllBytes(corrupt, [1, 2, 3]);
            string oversized = Path.Combine(files.Root, "oversized.png");
            using (var output = File.Create(oversized)) output.SetLength(8 * 1024 * 1024 + 1);
            foreach (string path in new[] { "relative.png", @"\\server\share\never-contact.png", corrupt,
                         oversized, Path.Combine(files.Root, "missing.png") })
                Assert.Null(Wait(loader.LoadAsync(path)));
            Assert.Equal(0, loader.CacheEntryCount);
        });
    }

    private static byte Blue(BitmapSource source)
    {
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        byte[] pixel = new byte[4];
        converted.CopyPixels(new Int32Rect(0, 0, 1, 1), pixel, 4, 0);
        return pixel[0];
    }

    private static T Wait<T>(Task<T> task) { Wait((Task)task); return task.GetAwaiter().GetResult(); }
    private static void Wait(Task task)
    {
        if (!task.IsCompleted)
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            var frame = new DispatcherFrame();
            var timeout = new DispatcherTimer(DispatcherPriority.Send) { Interval = TimeSpan.FromSeconds(12) };
            timeout.Tick += (_, _) => frame.Continue = false;
            _ = task.ContinueWith(_ => dispatcher.BeginInvoke(new Action(() => frame.Continue = false)), TaskScheduler.Default);
            timeout.Start();
            try { Dispatcher.PushFrame(frame); }
            finally { timeout.Stop(); }
            Assert.True(task.IsCompleted, "The bounded image operation did not settle.");
        }
        task.GetAwaiter().GetResult();
    }

    private static void InSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            try { action(); }
            catch (Exception error) { failure = error; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(45)), "The isolated thumbnail regression timed out.");
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private sealed class Images : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "Flick-AsyncStill-" + Guid.NewGuid().ToString("N"));
        public Images() => Directory.CreateDirectory(Root);
        public string Write(string name, int width, int height, byte blue)
        {
            byte[] pixels = new byte[width * height * 4];
            for (int i = 0; i < pixels.Length; i += 4) { pixels[i] = blue; pixels[i + 3] = 255; }
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(width, height, 96, 96,
                PixelFormats.Bgra32, null, pixels, width * 4)));
            string path = Path.Combine(Root, name);
            using (var output = File.Create(path)) encoder.Save(output);
            return path;
        }
        public void Dispose() { try { Directory.Delete(Root, true); } catch { } }
    }
}
