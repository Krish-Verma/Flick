using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Controls;
using System.Windows.Threading;
using LocalClip.App.ViewModels;
using LocalClip.App.Views.Components;
using LocalClip.Core.Media;
using LocalClip.Core.Models;
using LocalClip.Core.Storage;
using Xunit;

namespace LocalClip.App.Tests;

/// <summary>Runs inside the existing single WPF Application test; no second Application or capture helper.</summary>
internal static class OrganizationEditorAssertions
{
    public static void Run(MainViewModel vm)
    {
        const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        var field = typeof(MainViewModel).GetField("_storage", hidden)!;
        object? originalStorage = field.GetValue(vm);
        string root = Path.Combine(Path.GetTempPath(), "flick-editor-ui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string source = Path.Combine(root, "source.mkv"); byte[] bytes = [1, 2, 3, 4]; File.WriteAllBytes(source, bytes);
        string metadata = Path.Combine(root, "Metadata");
        var storage = new ClipStorage(new AppSettings(), metadataDirectory: metadata);
        var first = new ClipMetadata { Id = "first", Title = "First", OriginalFilePath = source, ThumbnailPath = source,
            DurationSeconds = 10, TrimEndSeconds = 10, Width = 640, Height = 360, Fps = 30 };
        var second = new ClipMetadata { Id = "second", Title = "Second", OriginalFilePath = source + "-offline",
            DurationSeconds = 10, TrimEndSeconds = 10, SourceKind = "Imported", ConnectedFolderId = "own-fixture" };
        storage.AddOrUpdateClip(first); storage.AddOrUpdateClip(second);
        try
        {
            field.SetValue(vm, storage); vm.CaptureStatus = "Off"; vm.ReloadClips(); vm.SelectedClip = first;
            vm.ToggleFavorite(); Assert.True(first.IsFavorite);
            vm.TagsDraft = "Clutch, clutch, Team"; vm.GameDraft = "Fixture Game";
            Assert.True(vm.HasUnsavedOrganization); Assert.True(vm.SaveOrganization()); Assert.False(vm.HasUnsavedOrganization);
            Assert.Equal(new[] { "Clutch", "Team" }, first.Tags);
            Assert.True(vm.ApplyTrim(1, 8)); Assert.Contains("saved", vm.EditorSaveState);
            Assert.True(vm.UndoTrim()); Assert.Equal(0, first.TrimStartSeconds); Assert.Equal(10, first.TrimEndSeconds);
            Assert.True(vm.AddMarker(2.25)); Assert.False(vm.AddMarker(11)); Assert.Single(first.Markers);
            AssertTimelineKeepsCommittedRangeOnInvalidInput();
            var proberField = typeof(MainViewModel).GetField("_prober", hidden)!;
            var originalProber = proberField.GetValue(vm);
            var delayedProber = new CancelledThumbnailProber();
            SynchronizationContext? previousContext = SynchronizationContext.Current;
            try
            {
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
                proberField.SetValue(vm, delayedProber);
                Task loading = vm.LoadFilmstripAsync();
                Assert.True(vm.IsLoadingFilmstrip); Assert.True(vm.IsBackgroundWorkActive);
                Assert.True(delayedProber.Started);
                vm.SelectedClip = second; // Cancels old work before its completion may publish images.
                Pump(loading); Assert.True(delayedProber.Cancelled);
                Assert.Empty(vm.FilmstripFrames); Assert.False(vm.IsLoadingFilmstrip);
                Assert.False(vm.IsBackgroundWorkActive); Assert.Same(second, vm.SelectedClip);
                vm.SelectedClip = first;
            }
            finally { proberField.SetValue(vm, originalProber); SynchronizationContext.SetSynchronizationContext(previousContext); }
            vm.ConfirmDiscardEditorChanges = () => false; vm.TagsDraft = "not saved";
            vm.SelectedClip = second; Assert.Same(first, vm.SelectedClip);
            vm.ConfirmDiscardEditorChanges = () => true; vm.SelectedClip = second; Assert.Same(second, vm.SelectedClip);
            // An old context-menu callback must not delete an equal-valued marker on a different clip.
            second.Markers = [new(2.25, "")]; vm.RemoveMarker(first.Markers[0]); Assert.Single(second.Markers);
            vm.SelectedClip = first;
            string blocked = Path.Combine(metadata, "clips_index.json.tmp"); Directory.CreateDirectory(blocked);
            Assert.False(vm.ApplyTrim(2, 7)); Assert.Equal(0, first.TrimStartSeconds); Assert.Equal(10, first.TrimEndSeconds);
            Assert.Contains("Couldn't save", vm.EditorSaveState);
            Directory.Delete(blocked);
            Assert.Equal(bytes, File.ReadAllBytes(source));
            File.Delete(source); vm.ReloadClips();
            vm.SearchQuery = "#clutch Fixture"; Assert.Contains(first, vm.FilteredClips); Assert.False(first.IsAvailable);
            vm.SearchQuery = "";
            var restored = new ClipStorage(new AppSettings(), metadataDirectory: metadata).GetClipById("first")!;
            Assert.True(restored.IsFavorite); Assert.Equal("Fixture Game", restored.GameName); Assert.Single(restored.Markers);
        }
        finally
        {
            vm.ConfirmDiscardEditorChanges = null; field.SetValue(vm, originalStorage);
            try { Directory.Delete(root, true); } catch { }
        }
    }

    private static void AssertTimelineKeepsCommittedRangeOnInvalidInput()
    {
        var timeline = new TrimRangeControl(); timeline.SetRange(10, 1, 8);
        int changes = 0, commits = 0, reverts = 0;
        timeline.RangeChanging += (_, _) => changes++;
        timeline.RangeCommitted += (_, _) => commits++;
        timeline.RangeReverted += (_, _) => reverts++;
        var start = (TextBox)timeline.FindName("StartTimeBox");
        var end = (TextBox)timeline.FindName("EndTimeBox");
        var commit = typeof(TrimRangeControl).GetMethod("CommitTimeBoxes", BindingFlags.NonPublic | BindingFlags.Instance)!;
        start.Text = "9"; end.Text = "2"; commit.Invoke(timeline, null);
        Assert.True(changes > 0); Assert.Equal(0, commits); Assert.Equal(1, reverts);
        Assert.Equal(1, timeline.SelectionStart); Assert.Equal(8, timeline.SelectionEnd);
        start.Text = "2"; end.Text = "7"; commit.Invoke(timeline, null);
        Assert.Equal(1, commits); Assert.Equal(2, timeline.SelectionStart); Assert.Equal(7, timeline.SelectionEnd);
    }

    private static void Pump(Task task)
    {
        var frame = new DispatcherFrame();
        Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
        var deadline = new DispatcherTimer(DispatcherPriority.Send) { Interval = TimeSpan.FromSeconds(5) };
        deadline.Tick += (_, _) => frame.Continue = false;
        _ = task.ContinueWith(_ => dispatcher.BeginInvoke(() => frame.Continue = false),
            CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
        deadline.Start();
        try { if (!task.IsCompleted) Dispatcher.PushFrame(frame); }
        finally { deadline.Stop(); }
        Assert.True(task.IsCompleted, "Cancelled filmstrip did not settle within its bound.");
        task.GetAwaiter().GetResult();
    }

    private sealed class CancelledThumbnailProber() : MediaProber("unused-ffprobe", "unused-ffmpeg")
    {
        public bool Started { get; private set; }
        public bool Cancelled { get; private set; }
        public override async Task<string> GenerateThumbnailAsync(string input, string output,
            double seconds = 1, CancellationToken cancellationToken = default)
        {
            Started = true;
            try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); return output; }
            catch (OperationCanceledException) { Cancelled = true; throw; }
        }
    }
}
