using System.Diagnostics;
using System.ComponentModel;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LocalClip.App;
using LocalClip.App.Services;
using LocalClip.App.ViewModels;
using LocalClip.Core.Media;
using LocalClip.Core.Ipc;
using LocalClip.Core.Models;
using LocalClip.Core.Process;
using LocalClip.Core.Rules;
using LocalClip.Core.Storage;

namespace Flick.UiRender;

internal static partial class Program
{
    private static void RenderShareFixtures(MainWindow window, MainViewModel vm, AppSettings settings,
        ClipStorage storage, string fixtures, string output, Action<string, int, int> render)
    {
        T Find<T>(string name) => (T)window.FindName(name);
        void Invoke(string method, params object[] values) => typeof(MainWindow).GetMethod(method, Private)!.Invoke(window, values);
        var bindingObservations = new List<object>();
        var notifications = new System.Collections.Concurrent.ConcurrentQueue<object>();
        PropertyChangedEventHandler observeNotification = (_, args) =>
        {
            if (notifications.Count < 256 && (args.PropertyName?.StartsWith("Share", StringComparison.Ordinal) == true ||
                args.PropertyName is nameof(vm.SelectedClip) or nameof(vm.IsExportReady) or nameof(vm.ExportStatusMessage)))
                notifications.Enqueue(new { Property = args.PropertyName, Thread = Environment.CurrentManagedThreadId,
                    OnDispatcher = window.Dispatcher.CheckAccess(), Title = vm.ShareClipTitle,
                    State = vm.ShareTileStateText, CanCopy = vm.ShareCanCopy, HasPreview = vm.ShareHasPreview });
        };
        vm.PropertyChanged += observeNotification;
        void ShareFrame(string file, int width, int height, bool requireReady = false)
        {
            render(file, width, height);
            ObserveShareBindings(window, vm, file, bindingObservations, notifications.ToArray(), output, requireMatch: true, requireReady);
        }
        ObserveShareBindings(window, vm, "entry before synthetic-media selection", bindingObservations, notifications.ToArray(), output, requireMatch: false);
        Find<Border>("RecordingHealthPanel").Visibility = Visibility.Collapsed;
        Invoke("ReturnToClipWorkspace");
        Invoke("CloseEditorContext");

        string ffmpeg = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../tools/ffmpeg/ffmpeg.exe"));
        string ffprobe = Path.Combine(Path.GetDirectoryName(ffmpeg)!, "ffprobe.exe");
        if (!File.Exists(ffmpeg) || !File.Exists(ffprobe)) throw new InvalidOperationException("Pinned media runtime is required for real Share Ready fixtures.");
        Directory.CreateDirectory(settings.OriginalsFolder);
        string original = Path.Combine(settings.OriginalsFolder, "Synthetic Share fixture.mkv");
        var start = new ProcessStartInfo(ffmpeg) { UseShellExecute = false, CreateNoWindow = true };
        foreach (string argument in new[] { "-v", "error", "-nostdin", "-f", "lavfi", "-i", "testsrc2=size=640x360:rate=30",
                     "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000", "-t", "3", "-c:v", "libx264",
                     "-preset", "ultrafast", "-pix_fmt", "yuv420p", "-c:a", "aac", "-y", original }) start.ArgumentList.Add(argument);
        using (var process = System.Diagnostics.Process.Start(start)!)
        using (var job = new JobObject())
        {
            if (!job.AssignProcess(process) && !process.HasExited) throw new InvalidOperationException("Cannot confine Share fixture generator.");
            if (!process.WaitForExit(30000)) { process.Kill(true); throw new TimeoutException("Share fixture generator timed out."); }
            if (process.ExitCode != 0) throw new InvalidOperationException("Share fixture generator failed.");
        }
        var prober = new MediaProber(ffprobe, ffmpeg);
        var clip = Task.Run(() => storage.RegisterNewOriginalAsync(original, "Synthetic media fixture", ["Combined Mix"])).GetAwaiter().GetResult();
        clip.Title = "Overtime finish · synthetic media fixture";
        var result = Task.Run(() => new ClipExporter(ffmpeg, prober).ExportForDiscordAsync(clip, settings)).GetAwaiter().GetResult();
        if (!result.Success) throw new InvalidOperationException("Real Share fixture export failed: " + result.ErrorMessage);
        clip.ExportedFilePath = result.ExportFilePath;
        clip.ExportedFileSizeBytes = result.FileSizeBytes;
        clip.LastExportFingerprint = result.Fingerprint;
        clip.IsExportReady = true;
        storage.AddOrUpdateClip(clip);
        vm.ReloadClips();
        vm.SelectedClip = vm.FilteredClips.Single(item => item.Id == clip.Id);
        SettleLibraryPresentation(vm, window.Dispatcher);
        if (!vm.TryGetReadyShareFile(out var ready)) throw new InvalidOperationException("Real exported fixture did not pass production readiness.");
        string originalHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(original)));

        // No Window is shown: these are actual WPF client controls plus real
        // media preparation. External OLE drag/Discord acceptance is separate.
        typeof(MainWindow).GetField("_layoutMode", Private)!.SetValue(window, WorkspaceLayoutMode.Split);
        Invoke("EditorTool_Click", Find<Button>("ToolShare"), new RoutedEventArgs());
        PumpUntil(() => !vm.IsBackgroundWorkActive && typeof(MainViewModel).GetField("_sharePreviewCancellation", Private)!.GetValue(vm) == null, window.Dispatcher);
        ShareFrame("37-share-ready-1080.png", 1920, 1080, requireReady: true);
        ShareFrame("38-share-ready-1440.png", 2560, 1440, requireReady: true);
        typeof(MainWindow).GetField("_layoutMode", Private)!.SetValue(window, WorkspaceLayoutMode.Compact);
        Invoke("ShowCompactEditor");
        ShareFrame("39-share-ready-720.png", 1280, 720, requireReady: true);
        ShareFrame("40-share-ready-minimum.png", 720, 480, requireReady: true);

        var observations = new List<object>();
        foreach (int percent in new[] { 100, 125, 150, 200 })
        {
            double scale = percent / 100d;
            var root = (FrameworkElement)window.Content;
            root.Measure(new Size(1280, 720)); root.Arrange(new Rect(0, 0, 1280, 720)); root.UpdateLayout();
            ObserveShareBindings(window, vm, $"share-ready-scale-{percent}.png", bindingObservations, notifications.ToArray(), output,
                requireMatch: true, requireReady: true);
            var bitmap = new RenderTargetBitmap((int)(1280 * scale), (int)(720 * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            bitmap.Render(root);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine(output, $"share-ready-scale-{percent}.png")); encoder.Save(file);
            observations.Add(new { ScalePercent = percent, LogicalViewport = "1280x720", RasterOnly = true,
                CopyEnabled = Find<Button>("BtnShareCopyFile").IsEnabled, TileWidth = Find<Border>("DragHandle").ActualWidth });
        }
        Invoke("ApplyShareAccessiblePalette", true);
        ShareFrame("41-share-ready-accessible-palette.png", 1280, 720, requireReady: true);
        Invoke("ApplyShareAccessiblePalette", false);

        // Exercise normal menu ownership without opening a native popup or
        // invoking the confirmed webhook action in a rendering job.
        var more = Find<Button>("ShareMoreButton");
        var menu = Find<ContextMenu>("ShareMoreMenu");
        menu.PlacementTarget = more;
        DrainDispatcherThroughContextIdle(window.Dispatcher);
        bool postingAvailable = menu.Items.OfType<MenuItem>().Any(item => item.Name == "ShareSendToChannel");
        if (!postingAvailable || !ReferenceEquals(menu.DataContext, vm) || menu.IsOpen)
            throw new InvalidOperationException("More must preserve its production context and optional confirmed posting action.");
        ShareFrame("68-share-more-minimum.png", 720, 480, requireReady: true);
        observations.Add(new { OptionalPostingAvailable = postingAvailable, MenuDataContextIsViewModel = ReferenceEquals(menu.DataContext, vm),
            NativeMenuOpened = false, PostingInvoked = false });

        var captureProfile = typeof(MainViewModel).GetField("_captureProfile", Private)!.GetValue(vm);
        var setCaptureProfile = typeof(MainViewModel).GetMethod("SetCaptureProfile", Private)!;
        try
        {
            setCaptureProfile.Invoke(vm, [new CaptureEncodingProfile("obs_x264", "CPU x264 H.264", true, 1280, 720, 30)]);
            if (string.IsNullOrWhiteSpace(vm.SoftwareFallbackWarning))
                throw new InvalidOperationException("The synthetic CPU profile must exercise the production warning.");
            ShareFrame("76-share-cpu-warning-minimum.png", 720, 480, requireReady: true);
            observations.Add(new { SyntheticCpuProfileOnly = true, Warning = vm.SoftwareFallbackWarning,
                ShareAtMinimumWithCpuWarning = true, CaptureStarted = false });
            RenderResourceFixtures(window, vm, output, render);
        }
        finally { setCaptureProfile.Invoke(vm, [captureProfile]); }

        int originalCap = vm.TargetSizeMb;
        byte[] originalPreferences = File.ReadAllBytes(AppSettings.GetSettingsFilePath());
        Find<ComboBox>("CmbExportCap").SelectedIndex = 4;
        Find<TextBox>("TxtCustomMb").Text = "250";
        Invoke("TryApplyCustomSize");
        if (vm.TargetSizeMb != 250) throw new InvalidOperationException("The real Custom limit control did not apply 250 MB.");
        ShareFrame("69-share-custom-minimum.png", 720, 480);
        vm.TargetSizeMb = originalCap;
        Invoke("UpdateSharePresetSelection");
        if (!originalPreferences.SequenceEqual(File.ReadAllBytes(AppSettings.GetSettingsFilePath())))
            throw new InvalidOperationException("Restoring the Custom fixture must restore the isolated preferences exactly.");
        foreach (int invalid in new[] { 0, 251 })
        {
            Find<ComboBox>("CmbExportCap").SelectedIndex = 4;
            Find<TextBox>("TxtCustomMb").Text = invalid.ToString();
            byte[] beforeInvalid = File.ReadAllBytes(AppSettings.GetSettingsFilePath());
            Invoke("BtnExport_Click", Find<Button>("BtnExport"), new RoutedEventArgs());
            ShareFrame(invalid == 0 ? "70-share-custom-zero-minimum.png" : "71-share-custom-over-limit-minimum.png", 720, 480);
            if (vm.IsExporting || vm.IsBackgroundWorkActive || vm.TargetSizeMb != originalCap ||
                Find<TextBox>("TxtCustomMb").Text != invalid.ToString() ||
                !beforeInvalid.SequenceEqual(File.ReadAllBytes(AppSettings.GetSettingsFilePath())))
                throw new InvalidOperationException("Invalid Custom input started work, changed preferences or hid the draft.");
        }
        Invoke("UpdateSharePresetSelection");
        vm.ActivateSharePresentation();

        vm.IsExporting = true; vm.IsExportReady = false; vm.ExportProgress = 42;
        vm.ExportStatusMessage = "Visual fixture: encoding a local MP4 (42%). No export is running for this state capture.";
        ShareFrame("42-share-exporting-minimum.png", 720, 480);
        vm.CancelShareExport();
        ShareFrame("43-share-cancelling-minimum.png", 720, 480);
        vm.IsExporting = false;
        vm.ExportStatusMessage = "Export Failed: Synthetic storage failure. Choose an accessible export folder and try again. Your original is unchanged.";
        ShareFrame("44-share-failed-minimum.png", 720, 480);
        vm.SelectedClip!.TrimStartSeconds = .2;
        vm.ActivateSharePresentation();
        ShareFrame("45-share-stale-minimum.png", 720, 480);
        vm.SelectedClip.TrimStartSeconds = 0;
        File.Move(original, original + ".offline-fixture");
        clip.RefreshAvailability();
        vm.ActivateSharePresentation();
        ShareFrame("46-share-offline-minimum.png", 720, 480);
        File.Move(original + ".offline-fixture", original);
        clip.RefreshAvailability();
        vm.SelectedClip = null;
        ShareFrame("47-share-empty-minimum.png", 720, 480);
        clip.Title = "Overtime finish · a deliberately long synthetic fixture title to check readable wrapping and stable actions at minimum window size";
        vm.SelectedClip = clip;
        vm.ActivateSharePresentation();
        ShareFrame("48-share-long-title-minimum.png", 720, 480);
        vm.IsExportReady = false;
        vm.ExportStatusMessage = "Export Failed: " + string.Join(" ", Enumerable.Repeat(
            "Synthetic storage failure; the selected output location could not be used. Your original stays unchanged.", 8));
        string fullFailure = vm.ExportStatusMessage;
        ShareFrame("72-share-long-failure-minimum.png", 720, 480);
        if (vm.ExportStatusMessage != fullFailure || vm.ShareStatusSummary == fullFailure ||
            !vm.ShareStatusSummary.Contains("Details", StringComparison.Ordinal))
            throw new InvalidOperationException("A long failure must keep its full message and show a concise Details route.");
        if (Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(original))) != originalHash)
            throw new InvalidOperationException("Share fixtures changed the immutable original.");
        // Close through the actual button. Compact Share temporarily replaces
        // the player/tools; leaving it must restore their visibility and
        // normal keyboard availability without discarding any feature.
        Find<Button>("CloseEditorTool").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        bool editorRestoredAfterClose = new[] { "PreviewSurface", "PlaybackControlsRow", "EditorToolBar" }
            .All(name => Find<FrameworkElement>(name).Visibility == Visibility.Visible) &&
            Find<Border>("EditorContextLayer").Visibility == Visibility.Collapsed;
        if (!editorRestoredAfterClose)
            throw new InvalidOperationException("Closing compact Share did not restore the player and editor tools.");
        File.WriteAllText(Path.Combine(output, "share-fixture-evidence.json"), JsonSerializer.Serialize(new {
            Source = "Generated testsrc2 +440Hz tone, no real capture", result.Success, result.FileSizeBytes,
            result.DurationSeconds, OriginalSha256 = originalHash,
            ExportSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(ready))),
            FullDecode = "Production exporter full decode passed; independent FFmpeg media workflow is separate",
            ClipboardOrExternalDragExecuted = false, ActualDiscordAcceptance = false,
            CompleteShareMustFitWithoutScrolling = true, InvalidCustomLimitsRefusedBeforeAnyWorker = true,
            EditorRestoredAfterClose = editorRestoredAfterClose,
            StatesExportingCancellingFailedAreVisualFixtures = true, Scales = observations
        }, new JsonSerializerOptions { WriteIndented = true }));
        vm.PropertyChanged -= observeNotification;
    }

    private static void PumpUntil(Func<bool> completed, Dispatcher dispatcher)
    {
        var timer = Stopwatch.StartNew();
        while (!completed() && timer.Elapsed < TimeSpan.FromSeconds(10))
        {
            DrainDispatcherThroughContextIdle(dispatcher);
            Thread.Sleep(5);
        }
        if (!completed()) throw new TimeoutException("Bounded Share still-image load did not settle.");
        DrainDispatcherThroughContextIdle(dispatcher);
    }
}
