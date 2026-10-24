using System.Reflection;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LocalClip.App.Services;
using Xunit;

namespace LocalClip.App.Tests;

public class ShareFileAndPreviewTests
{
    [Fact]
    public void RealFileDropRoundTripRejectsMissingFileAndPreviewDecodeStaysBounded()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            string root = Path.Combine(Path.GetTempPath(), "Flick-Share-Contract-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                // This regular sentinel tests OS file formats only. It is not
                // presented as a codec-valid export or decoder certification.
                string file = Path.Combine(root, "Unicode clip 私の試合.mp4");
                File.WriteAllBytes(file, [1, 2, 3, 4]);
                DataObject data = ShareFileTransfer.CreateFileData(file);
                Assert.True(data.GetDataPresent(DataFormats.FileDrop, false));
                Assert.False(data.GetDataPresent(DataFormats.UnicodeText, false));
                Assert.Equal([file], Assert.IsType<string[]>(data.GetData(DataFormats.FileDrop, false)));
                Clipboard.SetDataObject(data, false);
                Assert.Equal([file], Clipboard.GetFileDropList().Cast<string>().ToArray());
                Clipboard.Clear();
                File.Delete(file);
                Assert.Throws<IOException>(() => ShareFileTransfer.CreateFileData(file));
                Assert.Throws<ArgumentException>(() => ShareFileTransfer.CreateFileData("relative.mp4"));

                string poster = Path.Combine(root, "poster.png");
                var source = BitmapSource.Create(2000, 1200, 96, 96, PixelFormats.Bgra32, null,
                    new byte[2000 * 1200 * 4], 2000 * 4);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(source));
                using (var output = File.Create(poster)) encoder.Save(output);
                Type loader = typeof(ShareFileTransfer).Assembly.GetType("LocalClip.App.Services.SharePreviewLoader")!;
                var method = loader.GetMethod("LoadAsync", BindingFlags.Static | BindingFlags.Public)!;
                BitmapSource? Load(string path, CancellationToken token = default) =>
                    ((Task<BitmapSource?>)method.Invoke(null, [path, token])!).GetAwaiter().GetResult();
                var bitmap = Assert.IsAssignableFrom<BitmapSource>(Load(poster));
                Assert.True(bitmap.IsFrozen);
                Assert.Equal(480, bitmap.PixelWidth);
                Assert.InRange(bitmap.PixelHeight, 287, 289);
                Assert.True((long)bitmap.PixelWidth * bitmap.PixelHeight * 4 <= 480 * 480 * 4);
                File.Delete(poster); // OnLoad must release its source handle.
                Assert.Null(Load(poster));
                File.WriteAllText(poster, "not an image");
                Assert.Null(Load(poster));
                File.WriteAllBytes(poster, new byte[8 * 1024 * 1024 + 1]);
                Assert.Null(Load(poster));
                using var cancelled = new CancellationTokenSource();
                cancelled.Cancel();
                Assert.ThrowsAny<OperationCanceledException>(() => Load(poster, cancelled.Token));
            }
            catch (Exception error) { failure = error; }
            finally { Directory.Delete(root, true); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "Owned Share STA test timed out.");
        if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
