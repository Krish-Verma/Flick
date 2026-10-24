using System.IO;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LocalClip.App.Services;
using Xunit;
using Xunit.Abstractions;

namespace LocalClip.App.Tests;

public sealed class FilmstripFrameTests(ITestOutputHelper output)
{
    [Fact]
    public void SixGeneratedFramesHaveBoundedIndependentPixelStorage()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                long before = GC.GetTotalMemory(false);
                var images = new List<BitmapSource>();
                for (int i = 0; i < FilmstripFrameDecoder.FrameCount; i++)
                {
                    using var jpeg = GeneratedJpeg(320, 180);
                    images.Add(FilmstripFrameDecoder.Decode(jpeg));
                }
                long pixels = images.Sum(image => (long)image.PixelWidth * image.PixelHeight * 4);
                Assert.Equal(FilmstripFrameDecoder.MaximumRetainedPixelBytes, pixels);
                Assert.All(images, image => { Assert.True(image.IsFrozen); Assert.IsNotType<TransformedBitmap>(image); });
                // Streams are already disposed: final bitmaps retain only independent pixels.
                images[0].CopyPixels(new byte[160 * 90 * 4], 160 * 4, 0);
                output.WriteLine($"Six frames: retained decoded pixels={pixels}B; managed before={before}B; after={GC.GetTotalMemory(false)}B (concurrent runtime allocations included).");
                using var oversized = GeneratedJpeg(640, 360);
                Assert.Throws<InvalidDataException>(() => FilmstripFrameDecoder.Decode(oversized));
                using var portrait = GeneratedJpeg(90, 180);
                var fitted = FilmstripFrameDecoder.Decode(portrait);
                Assert.Equal(45, fitted.PixelWidth); Assert.Equal(90, fitted.PixelHeight);
            }
            catch (Exception error) { failure = error; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "Bounded WPF decode did not settle.");
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
    private static MemoryStream GeneratedJpeg(int width, int height)
    {
        byte[] pixels = new byte[width * height * 4];
        for (int i = 0; i < pixels.Length; i += 4) { pixels[i] = 64; pixels[i + 1] = 128; pixels[i + 2] = 192; pixels[i + 3] = 255; }
        var frame = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        var jpeg = new JpegBitmapEncoder(); jpeg.Frames.Add(BitmapFrame.Create(frame));
        var stream = new MemoryStream(); jpeg.Save(stream); stream.Position = 0; return stream;
    }
}
