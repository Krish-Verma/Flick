using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LocalClip.App.Services;
using LocalClip.App.ViewModels;
using LocalClip.Core.Models;
using LocalClip.Core.Rules;
using LocalClip.Core.Storage;
using Xunit;

namespace LocalClip.App.Tests;

/// <summary>Actual bindings/handlers in the existing unshown single-Application fixture. No capture, codec process or clipboard.</summary>
internal static class ResearchPresentationAssertions
{
    private const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Instance;

    public static void Run(MainWindow window, MainViewModel vm)
    {
        Assert.True(window.Dispatcher.CheckAccess());
        Assert.False(vm.CaptureHost.IsRunning);
        using var fixture = new OwnedLibrary(window, vm);
        AssertSearchResetAndLazyPoster(window, vm, fixture);
        AssertShareStagesAndActions(window, vm, fixture);
        fixture.AssertOwnedFilesUnchanged();
        Assert.False(vm.CaptureHost.IsRunning);
    }

    private static void AssertSearchResetAndLazyPoster(MainWindow window, MainViewModel vm, OwnedLibrary fixture)
    {
        var search = Find<TextBox>(window, "TxtClipSearch");
        var clear = Find<Button>(window, "BtnClearClipSearch");
        BindingExpression binding = Assert.IsType<BindingExpression>(search.GetBindingExpression(TextBox.TextProperty));
        Assert.Equal(180, binding.ParentBinding.Delay);
        vm.ClearLibrarySearch(); vm.ClearLibraryFilters();
        vm.SearchQuery = "Arena";
        Drain(window.Dispatcher);
        Assert.Equal(Visibility.Visible, clear.Visibility);

        // A real target edit is pending in the production Delay=180 binding.
        // Clicking the actual Clear control must cancel that delayed value and retain the binding.
        search.SetCurrentValue(TextBox.TextProperty, "pending old query");
        clear.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Same(binding, search.GetBindingExpression(TextBox.TextProperty));
        Assert.Equal("", search.Text);
        PumpFor(window.Dispatcher, TimeSpan.FromMilliseconds(240));
        Assert.Equal("", vm.SearchQuery);
        Assert.Equal(2, vm.FilteredClips.Count);

        search.SetCurrentValue(TextBox.TextProperty, "Arena");
        PumpFor(window.Dispatcher, TimeSpan.FromMilliseconds(240));
        Assert.Equal("Arena", vm.SearchQuery);
        Assert.Same(fixture.First, Assert.Single(vm.FilteredClips));
        clear.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Drain(window.Dispatcher);
        Assert.Same(binding, search.GetBindingExpression(TextBox.TextProperty));
        Assert.Equal(2, vm.FilteredClips.Count);

        vm.FavoritesOnly = true;
        vm.GameFilter = "Arena";
        vm.LibraryPeriod = "7 days";
        vm.ClipSourceFilter = "Flick";
        vm.LibrarySort = "Recently Exported";
        Drain(window.Dispatcher);
        Assert.Equal(5, vm.ActiveLibraryFilterCount);
        Assert.Equal("Filters (5)", Find<Button>(window, "BtnLibraryFilters").Content);
        InvokeWindow(window, "ClearLibraryFilters_Click", Find<Button>(window, "BtnLibraryFilters"));
        Drain(window.Dispatcher);
        Assert.False(vm.LibraryHasFilters);
        Assert.Equal("Filters", Find<Button>(window, "BtnLibraryFilters").Content);
        Assert.True(Find<RadioButton>(window, "RadioAllClips").IsChecked);
        Assert.Equal("Newest", vm.LibrarySort);
        Assert.Equal(2, vm.FilteredClips.Count);

        vm.SearchQuery = "does not match";
        Drain(window.Dispatcher);
        var empty = Find<StackPanel>(window, "LibraryEmptyState");
        Assert.Equal(Visibility.Visible, empty.Visibility);
        Assert.Equal("No matching clips", Assert.IsType<TextBlock>(empty.Children[0]).Text);
        clear.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Drain(window.Dispatcher);
        Assert.Equal(Visibility.Collapsed, empty.Visibility);

        vm.SelectedClip = fixture.First;
        LibraryObservationAssertions.Settle(vm);
        Drain(window.Dispatcher);
        var media = Find<MediaElement>(window, "VideoPlayer");
        var poster = Find<Image>(window, "PlayerPoster");
        Assert.Null(media.Source);
        Assert.False((bool)WindowField(window, "_playerIsPlaying")!);
        Assert.Equal(Visibility.Visible, poster.Visibility);
        BindingExpression posterBinding = Assert.IsType<BindingExpression>(poster.GetBindingExpression(AsyncThumbnail.PathProperty));
        Assert.Equal("SelectedClip.ThumbnailPath", posterBinding.ParentBinding.Path.Path);
        Assert.Equal(fixture.First.ThumbnailPath, AsyncThumbnail.GetPath(poster));
        Assert.Null(poster.Source); // The unshown fixture must not start a decorative decoder/load.
        InvokeWindow(window, "ReleasePosterWhileHidden");
        Assert.Null(poster.GetBindingExpression(AsyncThumbnail.PathProperty));
        Assert.Empty(AsyncThumbnail.GetPath(poster));
        InvokeWindow(window, "RestorePosterAfterShow");
        Drain(window.Dispatcher);
        Assert.NotNull(poster.GetBindingExpression(AsyncThumbnail.PathProperty));
        Assert.Equal(fixture.First.ThumbnailPath, AsyncThumbnail.GetPath(poster));
        Assert.Null(media.Source);
        Assert.False((bool)WindowField(window, "_playerIsPlaying")!);

        fixture.SetClips([]);
        Drain(window.Dispatcher);
        Assert.Equal("No clips yet", Assert.IsType<TextBlock>(empty.Children[0]).Text);
        fixture.SetClips([fixture.First, fixture.Offline]);
        vm.ClearLibrarySearch();
    }

    private static void AssertShareStagesAndActions(MainWindow window, MainViewModel vm, OwnedLibrary fixture)
    {
        vm.SelectedClip = fixture.First;
        LibraryObservationAssertions.Settle(vm);
        vm.IsExportReady = false;
        vm.IsExporting = false;
        Drain(window.Dispatcher);
        var export = Find<Button>(window, "BtnExport");
        var copy = Find<Button>(window, "BtnShareCopyFile");
        var cancel = Find<Button>(window, "BtnShareCancel");
        var tile = Find<TextBlock>(window, "TxtShareTileState");
        Assert.Same(window.FindResource("Flick.Button.Primary"), export.Style);
        Assert.Same(window.FindResource("Flick.Button.Secondary"), copy.Style);
        Assert.False(copy.IsEnabled);

        vm.IsExporting = true;
        vm.ExportProgress = 73; // A real encoding percentage must not masquerade as whole-operation completion.
        foreach (var (stage, expected) in new[]
        {
            ("Calculating bitrate budget (Attempt 1/3)...", "Sizing MP4…"),
            ("Encoding MP4 with CPU compatibility (1280x720 @ 1000 kbps)...", "Encoding MP4…"),
            ("Validating exported streams and duration...", "Checking MP4 streams…"),
            ("Running full decode corruption check...", "Checking playback…"),
            ("Finalizing share file...", "Finishing MP4…")
        })
        {
            // These are the actual ClipExporter stage contracts, projected through the real bound tile.
            // No FFmpeg operation or codec qualification is represented by this fixture.
            vm.ExportStatusMessage = stage;
            Drain(window.Dispatcher);
            Assert.Equal(expected, tile.Text);
            Assert.DoesNotContain("%", tile.Text);
            Assert.DoesNotContain("ETA", tile.Text, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(vm.ShareStatusSummary);
            Assert.True(Find<ProgressBar>(window, "ShareExportProgress").IsIndeterminate);
            Assert.False(copy.IsEnabled);
        }
        using var cancellation = new CancellationTokenSource();
        fixture.Set("_exportCancellation", cancellation);
        cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Drain(window.Dispatcher);
        Assert.True(cancellation.IsCancellationRequested);
        Assert.False(cancel.IsEnabled);
        Assert.Equal("Finishing cancellation…", tile.Text);
        vm.ExportStatusMessage = "Running full decode corruption check..."; // Late progress cannot erase cancellation.
        Drain(window.Dispatcher);
        Assert.Equal("Finishing cancellation…", tile.Text);
        Assert.False(copy.IsEnabled);
        vm.ExportStatusMessage = "Export cancelled. Your original stays unchanged.";
        vm.IsExporting = false;
        fixture.Set("_exportCancellation", null);
        Drain(window.Dispatcher);
        Assert.Equal("Export cancelled", tile.Text);

        fixture.PrepareReadySentinel();
        vm.RecheckShareReadiness();
        LibraryObservationAssertions.Settle(vm);
        Drain(window.Dispatcher);
        Assert.True(vm.IsExportReady);
        Assert.True(copy.IsEnabled);
        Assert.Equal("MP4 ready", tile.Text);
        Assert.Same(window.FindResource("Flick.Button.Primary"), copy.Style);
        Assert.Same(window.FindResource("Flick.Button.Secondary"), export.Style);
        // No Copy or Drag command is invoked: this is a last-mile metadata/file sentinel, not codec-valid video.
        File.Delete(fixture.First.ExportedFilePath);
        vm.RecheckShareReadiness();
        LibraryObservationAssertions.Settle(vm);
        Drain(window.Dispatcher);
        Assert.False(copy.IsEnabled);
        Assert.NotEqual("MP4 ready", tile.Text);
        Assert.Same(window.FindResource("Flick.Button.Primary"), export.Style);

        vm.ExportStatusMessage = "Export failed: " + new string('x', 100);
        Drain(window.Dispatcher);
        Assert.Equal("Export failed", tile.Text);
        Assert.Contains("Details", vm.ShareStatusSummary);
        Assert.False(copy.IsEnabled);
        vm.SelectedClip = fixture.Offline;
        LibraryObservationAssertions.Settle(vm);
        Drain(window.Dispatcher);
        Assert.Equal("Original offline", tile.Text);
        Assert.Contains("Reconnect", vm.ShareTileHint);
        Assert.False(copy.IsEnabled);
        Assert.False(export.IsEnabled);
        vm.SelectedClip = null;
        Drain(window.Dispatcher);
        Assert.Equal("No clip selected", tile.Text);
        Assert.False(copy.IsEnabled);
        Assert.Null(Find<MediaElement>(window, "VideoPlayer").Source);
    }

    private static T Find<T>(MainWindow window, string name) => Assert.IsType<T>(window.FindName(name));
    private static object? WindowField(MainWindow window, string name) => typeof(MainWindow).GetField(name, Hidden)!.GetValue(window);
    private static void InvokeWindow(MainWindow window, string name, object? sender = null) =>
        typeof(MainWindow).GetMethod(name, Hidden)!.Invoke(window, sender == null ? null : [sender, new RoutedEventArgs()]);
    private static void Drain(Dispatcher dispatcher)
    {
        for (int pass = 0; pass < 3; pass++)
            dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle, CancellationToken.None, TimeSpan.FromSeconds(2));
    }
    private static void PumpFor(Dispatcher dispatcher, TimeSpan interval)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = interval };
        timer.Tick += (_, _) => frame.Continue = false;
        timer.Start();
        try { Dispatcher.PushFrame(frame); }
        finally { timer.Stop(); }
        Drain(dispatcher);
    }

    private sealed class OwnedLibrary : IDisposable
    {
        private readonly MainWindow _window;
        private readonly MainViewModel _vm;
        private readonly Dictionary<string, object?> _before;
        private readonly string[] _games;
        private readonly string _exportsBefore;
        private readonly string _preferences;
        private readonly string _root = Path.Combine(Path.GetTempPath(), "Flick-Research-Bindings-" + Guid.NewGuid().ToString("N"));
        private readonly Dictionary<string, byte[]> _originalBytes = [];
        public ClipMetadata First { get; }
        public ClipMetadata Offline { get; }
        public OwnedLibrary(MainWindow window, MainViewModel vm)
        {
            LibraryObservationAssertions.Settle(vm);
            _window = window; _vm = vm;
            string[] fields = ["_storage", "_allClips", "_selectedClip", "_searchQuery", "_clipSourceFilter", "_favoritesOnly", "_gameFilter",
                "_libraryPeriod", "_librarySort", "_targetSizeMb", "_isExportReady", "_readyExportFilePath", "_exportStatusMessage", "_isExporting",
                "_shareCancellationRequested", "_sharePresentationActive", "_shareObservedClip", "_sharePreview", "_exportCancellation", "_exportProgress",
                "_tagsDraft", "_gameDraft", "_editorSaveState"];
            _before = fields.ToDictionary(name => name, Get);
            Assert.False(vm.IsExporting);
            Assert.Null(Get("_sharePreviewCancellation"));
            Assert.False((bool)Get("_sharePresentationActive")!);
            Assert.Null(Get("_pendingAudioClip"));
            _games = vm.GameGroups.ToArray();
            _exportsBefore = vm.Settings.ExportsFolder;
            _preferences = JsonSerializer.Serialize(vm.Settings);
            Directory.CreateDirectory(_root);
            string source = Path.Combine(_root, "source.mkv"), poster = Path.Combine(_root, "poster.png");
            File.WriteAllBytes(source, [1, 2, 3, 4]);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { 12, 40, 90, 255 }, 4)));
            using (var output = File.Create(poster)) encoder.Save(output);
            First = new ClipMetadata { Id = "research-first", Title = "Arena finish", GameName = "Arena", IsFavorite = true,
                OriginalFilePath = source, ThumbnailPath = poster, DurationSeconds = 10, TrimEndSeconds = 10, CreatedAt = DateTime.Now };
            Offline = new ClipMetadata { Id = "research-offline", Title = "Other imported round", GameName = "Other", SourceKind = "Imported",
                OriginalFilePath = source + ".offline", ThumbnailPath = poster, DurationSeconds = 10, TrimEndSeconds = 10, CreatedAt = DateTime.Now };
            var storage = new ClipStorage(new AppSettings(), metadataDirectory: Path.Combine(_root, "Metadata"));
            storage.AddOrUpdateClip(First); storage.AddOrUpdateClip(Offline);
            foreach (string file in Directory.GetFiles(_root, "*", SearchOption.AllDirectories)) _originalBytes[file] = File.ReadAllBytes(file);
            InvokeVm("UnsubscribeAudioEvents");
            Set("_selectedClip", null); Set("_sharePresentationActive", false);
            Set("_storage", storage); Set("_targetSizeMb", 20);
            vm.Settings.ExportsFolder = Path.Combine(_root, "Exports");
            Directory.CreateDirectory(vm.Settings.ExportsFolder);
            vm.GameGroups.Clear(); vm.GameGroups.Add("All Games"); vm.GameGroups.Add("Arena"); vm.GameGroups.Add("Other");
            SetClips([First, Offline]);
            vm.ClearLibraryFilters(); vm.ClearLibrarySearch();
            InvokeWindow(window, "RestorePosterAfterShow");
        }
        public void SetClips(List<ClipMetadata> clips) { Set("_allClips", clips); _vm.ApplyFilter(); }
        public void Set(string name, object? value) => typeof(MainViewModel).GetField(name, Hidden)!.SetValue(_vm, value);
        private object? Get(string name) => typeof(MainViewModel).GetField(name, Hidden)!.GetValue(_vm);
        private void InvokeVm(string name, params object?[] values) => typeof(MainViewModel).GetMethod(name, Hidden)!.Invoke(_vm, values);
        public void PrepareReadySentinel()
        {
            First.ExportedFilePath = Path.Combine(_vm.Settings.ExportsFolder,
                ShareExportNaming.ForOriginal(First.OriginalFilePath, 20, First.ExportAspectPreset));
            File.WriteAllBytes(First.ExportedFilePath, [7, 8, 9]);
            First.ExportedFileSizeBytes = 3; First.LastExportTargetMb = 20;
            First.LastExportFingerprint = ExportFingerprint.Compute(First.OriginalFilePath, First.TrimStartSeconds,
                First.TrimEndSeconds, First.AudioTracks, 20, First.ExportAspectPreset);
            First.IsExportReady = true;
        }
        public void AssertOwnedFilesUnchanged()
        {
            foreach (var (file, bytes) in _originalBytes) Assert.Equal(bytes, File.ReadAllBytes(file));
        }
        public void Dispose()
        {
            LibraryObservationAssertions.Settle(_vm);
            InvokeVm("UnsubscribeAudioEvents");
            _vm.Settings.ExportsFolder = _exportsBefore;
            _vm.GameGroups.Clear(); foreach (string game in _games) _vm.GameGroups.Add(game);
            foreach (var (name, value) in _before) Set(name, value);
            InvokeVm("SubscribeAudioEvents");
            _vm.ApplyFilter();
            foreach (string name in new[] { nameof(MainViewModel.SelectedClip), nameof(MainViewModel.SearchQuery), nameof(MainViewModel.IsExporting),
                         nameof(MainViewModel.IsExportReady), nameof(MainViewModel.ClipSourceFilter), nameof(MainViewModel.FavoritesOnly),
                         nameof(MainViewModel.GameFilter), nameof(MainViewModel.LibraryPeriod), nameof(MainViewModel.LibrarySort) })
                InvokeVm("OnPropertyChanged", name);
            Drain(_window.Dispatcher);
            Assert.Equal(_preferences, JsonSerializer.Serialize(_vm.Settings));
            try { Directory.Delete(_root, true); } catch { }
        }
    }
}
