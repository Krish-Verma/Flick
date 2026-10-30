using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
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
using LocalClip.App.Views;
using LocalClip.Core.Audio;
using LocalClip.Core.Models;
using LocalClip.Core.Storage;

namespace Flick.UiBenchmark;

internal static class Program
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private const int ContainerLimit = 96;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    [STAThread]
    private static int Main(string[] args)
    {
        string? output = null;
        string? profileRoot = null;
        string? marker = null;
        Application? app = null;
        Window? host = null;
        MainViewModel? vm = null;
        string token = Guid.NewGuid().ToString("N");
        try
        {
            Require(OperatingSystem.IsWindows() && Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true",
                "Synthetic benchmark requires an isolated Windows GitHub Actions runner.");
            Require(args.Length == 5, "Arguments: output.json variant revision rowCount sampleSeconds");
            output = Path.GetFullPath(args[0]);
            string variant = args[1], revision = args[2];
            int rowCount = int.Parse(args[3]), sampleSeconds = int.Parse(args[4]);
            Require(variant is "baseline" or "candidate", "Unknown variant");
            Require(new[] { 0, 50, 1000, 5000 }.Contains(rowCount), "Unsupported deterministic row count");
            Require(sampleSeconds is >= 15 and <= 300, "Sample duration must be 15–300 seconds");
            Require(!File.Exists(output), "Refusing to replace existing benchmark evidence");
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            var deadline = Stopwatch.StartNew();

            // Windows known-folder APIs need not honor a LOCALAPPDATA override.
            // Refuse the entire existing app directory before calling any Flick
            // API that creates it. Every created profile/index remains marker-owned.
            profileRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LocalClip");
            Require(!Directory.Exists(profileRoot) && !File.Exists(profileRoot),
                "Existing Flick profile directory: use a fresh runner; nothing was replaced.");
            Directory.CreateDirectory(profileRoot);
            marker = Path.Combine(profileRoot, ".synthetic-benchmark-owner");
            File.WriteAllText(marker, token);
            var settings = AppSettings.CreateDefault();
            settings.StorageRoot = Path.Combine(profileRoot, "SyntheticStorage");
            settings.OriginalsFolder = Path.Combine(settings.StorageRoot, "Originals");
            settings.ExportsFolder = Path.Combine(settings.StorageRoot, "Exports");
            settings.TempFolder = Path.Combine(settings.StorageRoot, "Temp");
            settings.ShowResourceUsage = false;
            settings.StartWithWindows = false;
            settings.StartMinimizedToTray = false;
            settings.KeepReplayReadyInBackground = false;
            settings.GameDetectionEnabled = false;
            settings.SaveToFile(AppSettings.GetSettingsFilePath());

            app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            foreach (string dictionary in new[] { "Colors", "Metrics", "Typography", "Icons", "Controls", "Chrome" })
                app.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri($"/Flick;component/Resources/Theme/Flick.{dictionary}.xaml", UriKind.Relative)
                });
            var main = new MainWindow();
            // Application.Shutdown also closes unshown windows. Remove the
            // production close-to-tray hook before any dispatcher work so cleanup
            // cannot apply a background-replay intent in this synthetic process.
            var closing = typeof(MainWindow).GetMethod("Window_Closing", Private)
                ?? throw new InvalidOperationException("Unshown-window cleanup contract changed");
            main.Closing -= (CancelEventHandler)closing.CreateDelegate(typeof(CancelEventHandler), main);
            vm = (MainViewModel)main.DataContext;
            StopTimers(vm);
            StopTimers(main);
            Require(!main.IsLoaded && !main.IsVisible, "Real MainWindow must remain unshown");
            var root = (FrameworkElement)main.Content;
            main.Content = null;
            root.DataContext = vm;
            if (root is Panel panel) panel.Background = main.Background;
            host = new Window
            {
                Width = 1240, Height = 760, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false, Title = "Flick synthetic WPF benchmark",
                Content = root, DataContext = vm, Resources = main.Resources, Background = main.Background
            };
            var list = (ListBox)main.FindName("ClipList");
            var search = (TextBox)main.FindName("TxtClipSearch");
            // Same 64 bounded, deterministic PNGs exercise the production synchronous
            // baseline and asynchronous candidate row-still paths. No video exists.
            var stillPaths = WriteSyntheticStills(profileRoot);
            // Seed the in-memory storage list in one O(N) AddRange, without an
            // index write per row. Empty originals deliberately model offline
            // source metadata; File.Exists("") returns before any filesystem IO.
            // Production ReloadClips then builds actual facets/summary/filter state.
            var clips = Enumerable.Range(0, rowCount).Select(index => new ClipMetadata
            {
                Id = $"benchmark-{index:D5}",
                Title = index % 10 == 0 ? $"Synthetic clutch clip {index:D5}" : $"Synthetic round {index:D5}",
                CreatedAt = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(-index),
                DurationSeconds = 30, TrimEndSeconds = 30, Width = 1920, Height = 1080, Fps = 60,
                GameName = $"Synthetic game {index % 8}", Tags = index % 4 == 0 ? ["team"] : [],
                OriginalFilePath = "", ThumbnailPath = stillPaths[index % stillPaths.Length]
            }).ToList();
            var storage = (ClipStorage)typeof(MainViewModel).GetField("_storage", Private)!.GetValue(vm)!;
            var storedClips = (List<ClipMetadata>)typeof(ClipStorage).GetField("_clips", Private)!.GetValue(storage)!;
            storedClips.AddRange(clips);
            vm.ReloadClips();
            typeof(MainWindow).GetMethod("ApplyWorkspaceLayout", Private)!.Invoke(main, null);
            host.Show();
            Settle();
            Require(!main.IsLoaded && !main.IsVisible, "MainWindow lifecycle escaped inert host");
            Require(TimersStopped(vm) && TimersStopped(main), "Production timers restarted in the inert host");
            Require(!Descendants<MediaElement>(root).Any(media => media.Source != null), "A media source was loaded");
            Require(vm.SelectedClip == null && vm.CaptureHost.HostProcessId == null,
                "Benchmark must not select media or start a capture host");
            Require(list.IsVisible && list.Items.Count == rowCount && storedClips.Count == rowCount,
                "Production library binding/storage is not visible/current");
            Require(VirtualizingPanel.GetIsVirtualizing(list) &&
                VirtualizingPanel.GetVirtualizationMode(list) == VirtualizationMode.Recycling,
                "Production library must retain virtualization and recycling");
            Require(Descendants<VirtualizingStackPanel>(list).Any(), "No actual virtualizing items panel");
            int initialContainers = CheckContainers(list, rowCount);
            var scroll = Descendants<ScrollViewer>(list).First();
            Require(scroll.CanContentScroll, "Library logical scrolling is disabled");

            // Warm production bindings/templates, a query, a scroll and a Settings
            // construction before collecting timings. No forced GC is used.
            vm.SearchQuery = "clutch";
            Settle();
            vm.SearchQuery = "";
            Settle();
            scroll.ScrollToBottom(); Settle(); WaitForRowStills(list, rowCount);
            scroll.ScrollToTop(); Settle(); WaitForRowStills(list, rowCount);
            OpenSettings(host, root, settings);
            Wait(TimeSpan.FromSeconds(3));
            WaitForRowStills(list, rowCount);
            var stillObservations = new List<object> { ObserveRowStills(list, rowCount, "after-warmup") };
            var query = new List<object>();
            var queryPostDebounce = new List<double>();
            var scrollMs = new List<double>();
            var scrollAndStillsReadyMs = new List<double>();
            var layoutMs = new List<double>();
            var settingsMs = new List<double>();
            var containers = new List<int> { initialContainers };
            for (int repetition = 0; repetition < 3; repetition++)
            {
                foreach (string term in new[] { "clutch", "#team", "flick-benchmark-no-match", "" })
                {
                    int expected = term switch
                    {
                        "clutch" => clips.Count(clip => clip.Title.Contains("clutch", StringComparison.Ordinal)),
                        "#team" => clips.Count(clip => clip.Tags.Contains("team")),
                        "" => rowCount,
                        _ => 0
                    };
                    var watch = Stopwatch.StartNew();
                    search.Text = term; // Real production binding includes 180ms debounce.
                    while (vm.SearchQuery != term)
                    {
                        Require(watch.Elapsed < TimeSpan.FromSeconds(5), "Search binding did not settle");
                        Wait(TimeSpan.FromMilliseconds(10));
                    }
                    Settle();
                    query.Add(new { term, milliseconds = watch.Elapsed.TotalMilliseconds, expected, actual = list.Items.Count });
                    Require(list.Items.Count == expected, "Search returned an incorrect deterministic count");
                    watch.Restart();
                    vm.ApplyFilter(); // Separately labelled filter+binding/layout, after debounce.
                    Settle();
                    queryPostDebounce.Add(watch.Elapsed.TotalMilliseconds);
                    WaitForRowStills(list, expected);
                    containers.Add(CheckContainers(list, expected));
                }
                foreach (double position in new[] { 0d, .5, 1, .25, .75, 0, .9, 0 })
                {
                    var watch = Stopwatch.StartNew();
                    scroll.ScrollToVerticalOffset(scroll.ScrollableHeight * position);
                    Settle();
                    scrollMs.Add(watch.Elapsed.TotalMilliseconds);
                    WaitForRowStills(list, rowCount);
                    scrollAndStillsReadyMs.Add(watch.Elapsed.TotalMilliseconds);
                    containers.Add(CheckContainers(list, rowCount));
                }
                var layoutWatch = Stopwatch.StartNew();
                root.InvalidateMeasure(); root.UpdateLayout(); Settle();
                layoutMs.Add(layoutWatch.Elapsed.TotalMilliseconds);
                settingsMs.Add(OpenSettings(host, root, settings));
            }
            // Let bounded transitions/JIT settle after scripted operations. Samples
            // cover this inert-host synthetic idle, not normal app polling/gameplay.
            Wait(TimeSpan.FromSeconds(3));
            WaitForRowStills(list, rowCount);
            stillObservations.Add(ObserveRowStills(list, rowCount, "before-idle-sampling"));
            var resources = new List<object>();
            using var process = Process.GetCurrentProcess();
            var idleClock = Stopwatch.StartNew();
            double previousCpu = process.TotalProcessorTime.TotalSeconds;
            double previousTime = idleClock.Elapsed.TotalSeconds;
            while (idleClock.Elapsed < TimeSpan.FromSeconds(sampleSeconds))
            {
                Wait(TimeSpan.FromSeconds(Math.Min(1, sampleSeconds - idleClock.Elapsed.TotalSeconds)));
                process.Refresh();
                double now = idleClock.Elapsed.TotalSeconds, cpu = process.TotalProcessorTime.TotalSeconds;
                resources.Add(new
                {
                    elapsedSeconds = now, timestampUtc = DateTimeOffset.UtcNow,
                    privateBytes = process.PrivateMemorySize64, workingSetBytes = process.WorkingSet64,
                    cpuTotalSeconds = cpu,
                    cpuOneCorePercent = 100 * Math.Max(0, cpu - previousCpu) / Math.Max(.001, now - previousTime),
                    handles = process.HandleCount, threads = process.Threads.Count,
                    managedBytes = GC.GetTotalMemory(false), allocatedBytes = GC.GetTotalAllocatedBytes(false),
                    gen0 = GC.CollectionCount(0), gen1 = GC.CollectionCount(1), gen2 = GC.CollectionCount(2)
                });
                previousCpu = cpu; previousTime = now;
                Require(deadline.Elapsed < TimeSpan.FromSeconds(sampleSeconds + 120), "Benchmark exceeded its internal budget");
            }
            Require(!main.IsLoaded && vm.SelectedClip == null && vm.CaptureHost.HostProcessId == null &&
                TimersStopped(vm) && TimersStopped(main) && !Descendants<MediaElement>(root).Any(media => media.Source != null),
                "Benchmark isolation changed during measurement");
            Require(clips.Count == rowCount && vm.FilteredClips.Count == rowCount, "Fixture changed during benchmark");
            stillObservations.Add(ObserveRowStills(list, rowCount, "after-idle-sampling"));
            var dpi = VisualTreeHelper.GetDpi(host);
            File.WriteAllText(output, JsonSerializer.Serialize(new
            {
                schemaVersion = 1, status = "passed", evidenceClass = sampleSeconds < 300
                    ? "synthetic-short-CI-diagnostic" : "synthetic-five-minute-protocol",
                variant, revision, rowCount, sampleSeconds, warmupSeconds = 6,
                runner = Environment.GetEnvironmentVariable("RUNNER_NAME"), os = Environment.OSVersion.ToString(),
                processors = Environment.ProcessorCount, dpiX = dpi.PixelsPerInchX, dpiY = dpi.PixelsPerInchY,
                viewport = new { width = host.ActualWidth, height = host.ActualHeight },
                runtime = Environment.Version.ToString(), applicationVersion = typeof(MainWindow).Assembly.GetName().Version?.ToString(),
                isolation = new { mainWindowLoaded = main.IsLoaded, hostProcessId = vm.CaptureHost.HostProcessId,
                    originalOrPlayableMedia = false, syntheticPngStillDecoding = true, timersStopped = TimersStopped(vm) && TimersStopped(main), closeToTrayHandlerDetached = true,
                    profile = "fresh-marker-owned-directory", forcedGc = false,
                    seedMethod = "single in-memory storage AddRange plus production ReloadClips; no index persistence" },
                limitations = "Synthetic offline originals with real bounded local PNG still decoding and inert host; process includes harness instrumentation; no normal app polling, capture, gameplay FPS, video decoding/playback, audio, hotkeys, Narrator or full per-monitor DPI acceptance. Timings are dispatcher/layout completion, not presented frame times. Hosted-runner noise is not a statistical performance claim.",
                maximumRealizedContainers = containers.Max(), containerLimit = ContainerLimit,
                queryKeystrokeToSettled = query, filterAndLayoutAfterDebounceMs = queryPostDebounce,
                scrollAndLayoutMs = scrollMs, scrollAndStillsReadyMs, layoutMs, settingsConstructionAndLayoutMs = settingsMs,
                syntheticStills = new
                {
                    fileCount = stillPaths.Length, pixelWidth = 256, pixelHeight = 144,
                    totalEncodedBytes = stillPaths.Sum(path => new FileInfo(path).Length),
                    assignment = "row-index modulo 64; originals absent; generated before timings; real production Image.Source decoding",
                    files = stillPaths.Select((path, index) => new
                    {
                        file = Path.GetFileName(path), encodedBytes = new FileInfo(path).Length,
                        sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))),
                        firstPixelBgra = StillPixel(index, 0, 0).Select(channel => (int)channel).ToArray()
                    }),
                    visibleRowObservations = stillObservations
                },
                resources, totalSeconds = deadline.Elapsed.TotalSeconds
            }, Json));
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            if (output != null && !File.Exists(output))
                File.WriteAllText(output, JsonSerializer.Serialize(new { schemaVersion = 1, status = "failed", error = error.ToString() }, Json));
            return 1;
        }
        finally
        {
            try { if (host != null) { host.Content = null; host.Close(); } vm?.Dispose(); app?.Shutdown(); }
            catch (Exception cleanupError) { Console.Error.WriteLine("WPF cleanup: " + cleanupError.Message); }
            if (profileRoot != null && marker != null && File.Exists(marker) && File.ReadAllText(marker) == token)
                Directory.Delete(profileRoot, true);
        }
    }

    private static string[] WriteSyntheticStills(string profileRoot)
    {
        string directory = Path.Combine(profileRoot, "SyntheticStills");
        Directory.CreateDirectory(directory);
        const int width = 256, height = 144, stride = width * 4;
        var paths = new string[64];
        var pixels = new byte[stride * height];
        for (int index = 0; index < paths.Length; index++)
        {
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int offset = y * stride + x * 4;
                pixels[offset] = (byte)(24 + (x * 3 + y + index * 17) % 160);
                pixels[offset + 1] = (byte)(32 + (y * 2 + index * 11) % 160);
                pixels[offset + 2] = (byte)(48 + (x + y * 3 + index * 7) % 160);
                pixels[offset + 3] = 255;
            }
            var source = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
            source.Freeze();
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            paths[index] = Path.Combine(directory, $"still-{index:D2}.png");
            using var file = new FileStream(paths[index], FileMode.CreateNew, FileAccess.Write);
            encoder.Save(file);
        }
        return paths;
    }

    private static byte[] StillPixel(int index, int x, int y) =>
    [
        (byte)(24 + (x * 3 + y + index * 17) % 160),
        (byte)(32 + (y * 2 + index * 11) % 160),
        (byte)(48 + (x + y * 3 + index * 7) % 160), 255
    ];

    private static Image[] RowImages(ListBox list) => Descendants<Image>(list)
        .Where(image => image.IsLoaded && image.IsVisible && image.DataContext is ClipMetadata).ToArray();

    // Observe the optional candidate loader without linking the common harness
    // against a type absent from baseline. Never call its snapshot/load methods.
    private static object? RowStillLoader()
    {
        var type = typeof(MainWindow).Assembly.GetType("LocalClip.App.Services.AsyncThumbnail");
        return type?.GetField("Loader", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null);
    }

    private static int? PendingRowStills()
    {
        var loader = RowStillLoader();
        return loader?.GetType().GetField("_pending", Private)?.GetValue(loader) as int?;
    }

    private static void WaitForRowStills(ListBox list, int items)
    {
        var watch = Stopwatch.StartNew();
        while (true)
        {
            Settle();
            var images = RowImages(list);
            if ((items == 0 || images.Length > 0) && images.All(image =>
                image.Source is BitmapSource { PixelWidth: 256, PixelHeight: 144 }) && (PendingRowStills() ?? 0) == 0)
                return;
            Require(watch.Elapsed < TimeSpan.FromSeconds(10), "Production row still decoding did not settle");
            Wait(TimeSpan.FromMilliseconds(10));
        }
    }

    private static object ObserveRowStills(ListBox list, int items, string phase)
    {
        WaitForRowStills(list, items);
        var images = RowImages(list);
        var rows = new List<object>();
        foreach (var image in images)
        {
            var clip = (ClipMetadata)image.DataContext;
            var source = (BitmapSource)image.Source;
            int index = int.Parse(Path.GetFileNameWithoutExtension(clip.ThumbnailPath)["still-".Length..]);
            Require(index is >= 0 and < 64, "Unexpected row still fixture path");
            var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            var samples = new List<object>();
            foreach (var point in new[] { new Int32Rect(0, 0, 1, 1), new Int32Rect(127, 71, 1, 1), new Int32Rect(255, 143, 1, 1) })
            {
                var pixel = new byte[4];
                converted.CopyPixels(point, pixel, 4, 0);
                Require(pixel.SequenceEqual(StillPixel(index, point.X, point.Y)), "Loaded row still pixels do not match current metadata");
                samples.Add(new { x = point.X, y = point.Y, bgra = pixel.Select(channel => (int)channel).ToArray() });
            }
            rows.Add(new { clipId = clip.Id, file = Path.GetFileName(clip.ThumbnailPath),
                pixelWidth = source.PixelWidth, pixelHeight = source.PixelHeight, frozen = source.IsFrozen,
                bitsPerPixel = source.Format.BitsPerPixel, pixels = samples });
        }
        var loader = RowStillLoader();
        int? entries = loader?.GetType().GetProperty("CacheEntryCount")?.GetValue(loader) as int?;
        Require(entries is null or <= 32, "Production row-still LRU exceeded 32 entries");
        return new { phase, loadedRowImages = images.Length,
            distinctVisibleBitmapSources = images.Select(image => (object)image.Source).Distinct(ReferenceEqualityComparer.Instance).Count(),
            decodedRowPixels = images.Sum(image => (long)((BitmapSource)image.Source).PixelWidth * ((BitmapSource)image.Source).PixelHeight),
            candidateLruEntries = entries, candidatePendingRequests = PendingRowStills(),
            loader = loader == null ? "baseline synchronous converter" : "production asynchronous loader",
            rows };
    }

    private static void StopTimers(object owner)
    {
        foreach (var field in owner.GetType().GetFields(Private).Where(field => field.FieldType == typeof(DispatcherTimer)))
            (field.GetValue(owner) as DispatcherTimer)?.Stop();
    }

    private static bool TimersStopped(object owner) => owner.GetType().GetFields(Private)
        .Where(field => field.FieldType == typeof(DispatcherTimer))
        .All(field => field.GetValue(owner) is not DispatcherTimer { IsEnabled: true });

    private static double OpenSettings(Window host, FrameworkElement workspace, AppSettings settings)
    {
        var watch = Stopwatch.StartNew();
        var page = new SettingsWindow(settings, () => false,
            loadAudioSources: () => Task.FromResult(new TargetList()),
            loadAudioEndpoints: _ => Array.Empty<AudioEndpoint>(),
            saveReviewedSettings: _ => throw new InvalidOperationException("Benchmark must not save preferences"),
            applyWindowsStartup: (_, _) => throw new InvalidOperationException("Benchmark must not write Windows startup"),
            readWindowsStartupStatus: _ => new WindowsStartupStatus(WindowsStartupRegistrationState.Registered, "Synthetic startup disabled"));
        host.Content = page;
        Settle();
        double milliseconds = watch.Elapsed.TotalMilliseconds;
        page.LeavePage();
        host.Content = workspace;
        Settle();
        return milliseconds;
    }

    private static int CheckContainers(ListBox list, int items)
    {
        int realized = Descendants<ListBoxItem>(list).Count();
        Require(realized <= Math.Min(items, ContainerLimit), $"Virtualization bound exceeded: {realized}/{items}");
        Require(items == 0 || realized > 0, "Non-empty library has no realized row containers");
        return realized;
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T typed) yield return typed;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private static void Settle()
    {
        for (int pass = 0; pass < 2; pass++)
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
    }

    private static void Wait(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero) return;
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = duration };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
