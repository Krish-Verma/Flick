using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Threading;
using LocalClip.App.ViewModels;
using LocalClip.Core.Media;
using LocalClip.Core.Models;
using LocalClip.Core.Storage;
using Xunit;

namespace LocalClip.App.Tests;

/// <summary>Uses the existing WPF Application and owned metadata; never starts a media process or capture host.</summary>
internal static class ThumbnailOperationAssertions
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

    public static void Run(MainViewModel vm)
    {
        FieldInfo Field(string name) => typeof(MainViewModel).GetField(name, Hidden)!;
        object? storageBefore = Field("_storage").GetValue(vm);
        object? selectionBefore = Field("_selectedClip").GetValue(vm);
        object? clipsBefore = Field("_allClips").GetValue(vm);
        object? tagsBefore = Field("_tagsDraft").GetValue(vm), gameBefore = Field("_gameDraft").GetValue(vm);
        object? saveStateBefore = Field("_editorSaveState").GetValue(vm);
        object? rescanBefore = Field("_libraryRescanOperations").GetValue(vm);
        string stateBefore = vm.CaptureStatus;
        bool exportingBefore = vm.IsExporting;
        var discardBefore = vm.ConfirmDiscardEditorChanges;
        SynchronizationContext? contextBefore = SynchronizationContext.Current;
        string root = Path.Combine(Path.GetTempPath(), "Flick-Thumbnail-Ownership-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string source = Path.Combine(root, "owned-source.mkv"); File.WriteAllBytes(source, [1, 2, 3]);
        var prober = new HeldCleanupProber();
        var storage = new ClipStorage(new AppSettings(), prober: prober, metadataDirectory: Path.Combine(root, "Metadata"));
        var first = new ClipMetadata { Id = "owned-first", OriginalFilePath = source, ThumbnailPath = source,
            DurationSeconds = 10, TrimEndSeconds = 10, Width = 640, Height = 360, Fps = 30 };
        var second = new ClipMetadata { Id = "owned-second", OriginalFilePath = source, ThumbnailPath = "",
            DurationSeconds = 10, TrimEndSeconds = 10, Width = 640, Height = 360, Fps = 30 };
        storage.AddOrUpdateClip(first); storage.AddOrUpdateClip(second);
        try
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            vm.ConfirmDiscardEditorChanges = () => true;
            vm.IsExporting = false; vm.CaptureStatus = "Off";
            Field("_storage").SetValue(vm, storage); Field("_selectedClip").SetValue(vm, null);
            vm.ReloadClips(); vm.SelectedClip = first;
            LibraryObservationAssertions.Settle(vm);
            Assert.Equal(0, prober.Starts); Assert.True(vm.CanStartExport); Assert.True(vm.CanLoadFilmstrip);

            // Re-selecting a missing-thumbnail clip enters the real VM/storage path.
            first.ThumbnailPath = ""; vm.SelectedClip = null; vm.SelectedClip = first;
            LibraryObservationAssertions.Settle(vm);
            Assert.Equal(1, prober.Starts); Assert.True(vm.IsBackgroundWorkActive);
            Task<bool> earlyStop = vm.StopSelectedThumbnailAsync(cleanupBudget: TimeSpan.FromMilliseconds(20));
            Pump(earlyStop);
            Assert.False(earlyStop.GetAwaiter().GetResult());
            Assert.True(prober.CancellationObserved.Task.IsCompletedSuccessfully);
            Assert.True(vm.IsBackgroundWorkActive);
            Assert.False(vm.CanControlReplay); Assert.False(vm.CanRunRecordingDiagnostic);
            Assert.False(vm.CanStartExport); Assert.False(vm.CanLoadFilmstrip);
            Assert.Throws<InvalidOperationException>(() => vm.CreateAudioMixPreviewAsync(first, CancellationToken.None).GetAwaiter().GetResult());
            vm.SelectedClip = second;
            LibraryObservationAssertions.Settle(vm);
            DrainDispatcher(); Assert.Equal(1, prober.Starts);

            // Cancellation is not completion: only explicit late cleanup releases ownership.
            prober.ReleaseCleanup.TrySetResult();
            Task<bool> settled = vm.StopSelectedThumbnailAsync(cleanupBudget: TimeSpan.FromSeconds(2));
            Pump(settled); Assert.True(settled.GetAwaiter().GetResult());
            Assert.False(vm.IsBackgroundWorkActive);
            Assert.Empty(first.ThumbnailPath); Assert.Empty(second.ThumbnailPath);
            Assert.All(prober.Outputs, output => Assert.False(File.Exists(output)));

            // Neither export nor unrelated background work may start a new worker on selection.
            vm.IsExporting = true; vm.SelectedClip = first; DrainDispatcher(); Assert.Equal(1, prober.Starts);
            vm.IsExporting = false; Field("_libraryRescanOperations").SetValue(vm, 1);
            Assert.True(vm.IsBackgroundWorkActive);
            vm.SelectedClip = second; DrainDispatcher(); Assert.Equal(1, prober.Starts);
            Field("_libraryRescanOperations").SetValue(vm, rescanBefore);
            Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(source));
        }
        finally
        {
            LibraryObservationAssertions.Settle(vm);
            prober.ReleaseCleanup.TrySetResult();
            Task<bool> cleanup = vm.StopSelectedThumbnailAsync(allowSettledShutdown: true, cleanupBudget: TimeSpan.FromSeconds(2));
            Pump(cleanup);
            Field("_storage").SetValue(vm, storageBefore); Field("_selectedClip").SetValue(vm, selectionBefore);
            Field("_allClips").SetValue(vm, clipsBefore); Field("_tagsDraft").SetValue(vm, tagsBefore);
            Field("_gameDraft").SetValue(vm, gameBefore); Field("_editorSaveState").SetValue(vm, saveStateBefore);
            Field("_libraryRescanOperations").SetValue(vm, rescanBefore);
            vm.IsExporting = exportingBefore; vm.CaptureStatus = stateBefore;
            vm.ConfirmDiscardEditorChanges = discardBefore; vm.ApplyFilter();
            SynchronizationContext.SetSynchronizationContext(contextBefore);
            try { Directory.Delete(root, true); } catch { }
        }
    }

    private static void DrainDispatcher() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);

    private static void Pump(Task task)
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        var frame = new DispatcherFrame();
        var timeout = new DispatcherTimer(DispatcherPriority.Send) { Interval = TimeSpan.FromSeconds(5) };
        timeout.Tick += (_, _) => frame.Continue = false;
        _ = task.ContinueWith(_ => dispatcher.BeginInvoke(new Action(() => frame.Continue = false)), TaskScheduler.Default);
        timeout.Start();
        try { if (!task.IsCompleted) Dispatcher.PushFrame(frame); }
        finally { timeout.Stop(); }
        Assert.True(task.IsCompleted, "Owned thumbnail cleanup did not settle within the test bound.");
        task.GetAwaiter().GetResult();
    }

    private sealed class HeldCleanupProber() : MediaProber("never-ffprobe", "never-ffmpeg")
    {
        public int Starts { get; private set; }
        public List<string> Outputs { get; } = [];
        public TaskCompletionSource CancellationObserved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseCleanup { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async Task<string> GenerateThumbnailAsync(string source, string output,
            double positionSec = 1, CancellationToken cancellationToken = default)
        {
            Starts++; Outputs.Add(output);
            if (Starts > 1) return ""; // Unexpected starts fail assertions without introducing another held task.
            using var observed = cancellationToken.Register(() => CancellationObserved.TrySetResult());
            try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
            catch (OperationCanceledException) { await ReleaseCleanup.Task; }
            cancellationToken.ThrowIfCancellationRequested();
            return "";
        }
    }
}
