using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using LocalClip.App;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LocalClip.App.ViewModels;
using LocalClip.Core.Models;
using Xunit;

namespace LocalClip.App.Tests;

/// <summary>Called from the existing single-Application STA fixture; creates no app, host or decoder process.</summary>
internal static class SharePresentationAssertions
{
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    internal static void AssertStaleNotificationsAndSamePathRepair(MainWindow window, MainViewModel vm)
    {
        var dispatcher = Application.Current?.Dispatcher ?? throw new InvalidOperationException("Use the existing Application fixture.");
        Assert.True(dispatcher.CheckAccess());
        FieldInfo Field(string name) => typeof(MainViewModel).GetField(name, Private)!;
        object? Get(string name) => Field(name).GetValue(vm);
        void Set(string name, object? value) => Field(name).SetValue(vm, value);
        object? Invoke(string name, params object?[] values) => typeof(MainViewModel).GetMethod(name, Private)!.Invoke(vm, values);
        void Notify(ClipMetadata clip) => Invoke("OnShareClipPropertyChanged", clip, new PropertyChangedEventArgs(nameof(ClipMetadata.ThumbnailPath)));
        Task Refresh(ClipMetadata clip) => (Task)Invoke("RefreshSharePreviewAsync", clip)!;
        string[] savedNames = ["_selectedClip", "_sharePresentationActive", "_shareDisposed", "_shareObservedClip", "_sharePreview", "_sharePreviewCancellation", "_isExportReady", "_readyExportFilePath", "_exportStatusMessage"];
        var saved = savedNames.ToDictionary(name => name, Get);
        Assert.Null(saved["_sharePreviewCancellation"]); // The fixture must be idle before taking ownership.
        var gate = (SemaphoreSlim)Get("_sharePreviewGate")!;
        Assert.True(gate.Wait(0));
        bool held = true;
        string root = Path.Combine(Path.GetTempPath(), "Flick-Share-State-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var currentCancellation = new CancellationTokenSource();
        var first = new ClipMetadata { Title = "Synthetic first" };
        var second = new ClipMetadata { Title = "Synthetic second" };
        var marker = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[4], 4);
        marker.Freeze();
        try
        {
            Set("_sharePresentationActive", true); Set("_shareDisposed", false);
            Set("_selectedClip", first); Set("_shareObservedClip", first);
            Notify(first); // Real queued callback from A, before switching to B.
            Set("_selectedClip", second); Set("_shareObservedClip", second);
            Set("_sharePreviewCancellation", currentCancellation); Set("_sharePreview", marker);
            dispatcher.Invoke(() => { }, DispatcherPriority.Background);
            Assert.Same(currentCancellation, Get("_sharePreviewCancellation"));
            Assert.False(currentCancellation.IsCancellationRequested);
            Assert.Same(marker, vm.SharePreview);
            Assert.True(Refresh(first).IsCompletedSuccessfully); // Entry fence, independently of the queued-event fence.
            Assert.Same(currentCancellation, Get("_sharePreviewCancellation"));
            Assert.Same(marker, vm.SharePreview);

            Notify(second); // Close while its real notification is still queued.
            Set("_sharePresentationActive", false); Set("_sharePreview", null);
            dispatcher.Invoke(() => { }, DispatcherPriority.Background);
            Assert.Same(currentCancellation, Get("_sharePreviewCancellation"));
            Assert.False(currentCancellation.IsCancellationRequested); Assert.Null(vm.SharePreview);

            Set("_sharePresentationActive", true);
            Notify(second); // Dispose while its real notification is still queued.
            Set("_shareDisposed", true);
            dispatcher.Invoke(() => { }, DispatcherPriority.Background);
            Assert.Same(currentCancellation, Get("_sharePreviewCancellation"));
            Assert.False(currentCancellation.IsCancellationRequested); Assert.Null(vm.SharePreview);

            Set("_shareDisposed", false); Set("_sharePreviewCancellation", null);
            gate.Release(); held = false;
            string poster = Path.Combine(root, "repaired-thumbnail.png");
            second.ThumbnailPath = poster; // Its cached path exists in metadata but bytes are missing.
            var missing = Refresh(second);
            PumpUntil(() => missing.IsCompleted, dispatcher); missing.GetAwaiter().GetResult();
            Assert.Null(vm.SharePreview);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(marker));
            using (var file = File.Create(poster)) encoder.Save(file);
            int pathChanges = 0;
            PropertyChangedEventHandler count = (_, args) => { if (args.PropertyName == nameof(ClipMetadata.ThumbnailPath)) pathChanges++; };
            second.PropertyChanged += count;
            try { second.ThumbnailPath = poster; } finally { second.PropertyChanged -= count; }
            Assert.Equal(0, pathChanges); // Regeneration at the same path emits no setter notification.
            vm.NotifyShareThumbnailAvailable(second);
            PumpUntil(() => Get("_sharePreviewCancellation") == null, dispatcher);
            var loaded = Assert.IsAssignableFrom<BitmapSource>(vm.SharePreview);
            Assert.True(loaded.IsFrozen); Assert.Equal(1, loaded.PixelWidth);
            File.Delete(poster); // Loaded still image owns no lingering file handle.

            // The selected object stays identical while its original goes
            // offline and returns. Exercise the actual Export button binding,
            // not merely the last-mile export guard or a copied bool value.
            string original = Path.Combine(root, "availability.mkv");
            File.WriteAllBytes(original, [1, 2, 3, 4]); // State-only sentinel; no codec claim.
            second.OriginalFilePath = original;
            second.ThumbnailPath = original; // Existing path prevents starting a decoder.
            var export = (Button)window.FindName("BtnExport");
            void RefreshAvailability()
            {
                second.RefreshAvailability();
                Invoke("OnSelectedClipChanged"); // Same production recheck used when Share opens.
                for (int pass = 0; pass < 3; pass++)
                    dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle,
                        CancellationToken.None, TimeSpan.FromSeconds(2));
            }
            RefreshAvailability();
            Assert.True(vm.CanStartExport); Assert.True(export.IsEnabled);
            File.Move(original, original + ".offline");
            RefreshAvailability();
            Assert.False(vm.CanEditClip); Assert.False(vm.CanStartExport); Assert.False(export.IsEnabled);
            Assert.Equal("Original offline", vm.ShareTileStateText);
            File.Move(original + ".offline", original);
            RefreshAvailability();
            Assert.True(vm.CanStartExport); Assert.True(export.IsEnabled);
        }
        finally
        {
            if (held) gate.Release();
            var pending = Get("_sharePreviewCancellation") as CancellationTokenSource;
            if (pending != null && !ReferenceEquals(pending, currentCancellation))
            {
                pending.Cancel();
                PumpUntil(() => Get("_sharePreviewCancellation") == null, dispatcher);
            }
            foreach (var pair in saved) Set(pair.Key, pair.Value);
            Directory.Delete(root, true);
        }
    }

    private static void PumpUntil(Func<bool> done, Dispatcher dispatcher)
    {
        var elapsed = Stopwatch.StartNew();
        while (!done() && elapsed.Elapsed < TimeSpan.FromSeconds(5))
        {
            dispatcher.Invoke(() => { }, DispatcherPriority.Background);
            Thread.Sleep(1);
        }
        Assert.True(done(), "The owned Share still-image operation did not settle.");
    }
}
