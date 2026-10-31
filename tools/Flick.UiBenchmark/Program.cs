using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
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
    private static readonly object TraceGate = new();
    private static readonly Stopwatch Lifetime = Stopwatch.StartNew();
    private static string? _tracePath;
    private static string? _outputPath;
    private static string _phase = "startup";
    private static Exception? _backgroundFailure;
    private static int _cleanupFinished;
    private static long _dispatcherStarted, _dispatcherCompleted;
    private static int _lastDispatcherPriority;
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<DispatcherOperation, (int Priority, long Started)> ActiveDispatcherOperations = new();

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
        string? resultJson = null;
        Exception? failure = null;
        System.Threading.Timer? deadlineWatchdog = null;
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
            _outputPath = output;
            _tracePath = output + ".phases.jsonl";
            Checkpoint("arguments-validated", new { variant, revision, rowCount, sampleSeconds });
            AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
                RecordFailure(eventArgs.ExceptionObject as Exception ?? new Exception(eventArgs.ExceptionObject?.ToString()), "runtime-unhandled");
            TaskScheduler.UnobservedTaskException += (_, eventArgs) =>
            {
                Interlocked.CompareExchange(ref _backgroundFailure, eventArgs.Exception, null);
                Checkpoint("task-unobserved-exception", new { error = eventArgs.Exception.ToString() });
            };
            // A dispatcher deadlock cannot service a DispatcherTimer timeout.
            // Keep the existing total budget, enforced independently of WPF.
            deadlineWatchdog = new System.Threading.Timer(_ => FatalBudget("process-budget-exceeded"), null,
                TimeSpan.FromSeconds(sampleSeconds + 120), Timeout.InfiniteTimeSpan);
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
            Checkpoint("throwaway-profile-created");

            app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.Dispatcher.Hooks.OperationStarted += (sender, eventArgs) =>
            {
                Interlocked.Increment(ref _dispatcherStarted);
                Volatile.Write(ref _lastDispatcherPriority, (int)eventArgs.Operation.Priority);
                ActiveDispatcherOperations[eventArgs.Operation] = ((int)eventArgs.Operation.Priority, Stopwatch.GetTimestamp());
            };
            app.Dispatcher.Hooks.OperationCompleted += (sender, eventArgs) =>
            {
                Interlocked.Increment(ref _dispatcherCompleted);
                ActiveDispatcherOperations.TryRemove(eventArgs.Operation, out _);
            };
            app.Dispatcher.Hooks.OperationAborted += (sender, eventArgs) => ActiveDispatcherOperations.TryRemove(eventArgs.Operation, out _);
            foreach (string dictionary in new[] { "Colors", "Metrics", "Typography", "Icons", "Controls", "Chrome" })
                app.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri($"/Flick;component/Resources/Theme/Flick.{dictionary}.xaml", UriKind.Relative)
                });
            app.DispatcherUnhandledException += (_, eventArgs) =>
            {
                RecordFailure(eventArgs.Exception, "dispatcher-unhandled");
                Environment.Exit(1); // Only this isolated owned process; retain trace and marker for diagnosis.
            };
            Checkpoint("construct-mainwindow");
            var main = new MainWindow();
            Checkpoint("mainwindow-constructed");
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
            Checkpoint("generate-synthetic-stills");
            var stillPaths = WriteSyntheticStills(profileRoot);
            Checkpoint("synthetic-stills-generated", new { count = stillPaths.Length });
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
            Checkpoint("library-seeded", new { count = storedClips.Count });
            typeof(MainWindow).GetMethod("ApplyWorkspaceLayout", Private)!.Invoke(main, null);
            Checkpoint("show-inert-host");
            host.Show();
            Settle();
            Checkpoint("inert-host-shown");
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
            var inputBinding = BindingOperations.GetBindingExpression(search, TextBox.TextProperty);
            Require(inputBinding is { Status: BindingStatus.Active } && inputBinding.ParentBinding.Delay == 180,
                "Actual delayed search binding is not active");
            var scroll = Descendants<ScrollViewer>(list).First();
            Require(scroll.CanContentScroll, "Library logical scrolling is disabled");

            // Warm production bindings/templates, a query, a scroll and a Settings
            // construction before collecting timings. No forced GC is used.
            Checkpoint("warmup-query");
            vm.SearchQuery = "clutch";
            Settle();
            vm.SearchQuery = "";
            Settle();
            scroll.ScrollToBottom(); Settle(); WaitForRowStills(list, rowCount, "warmup-bottom");
            scroll.ScrollToTop(); Settle(); WaitForRowStills(list, rowCount, "warmup-top");
            OpenSettings(host, root, settings);
            Wait(TimeSpan.FromSeconds(3));
            WaitForRowStills(list, rowCount, "warmup-complete");
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
                    Checkpoint("query-input", new { repetition, term });
                    Require(ReferenceEquals(inputBinding, BindingOperations.GetBindingExpression(search, TextBox.TextProperty)),
                        "Search binding was replaced before input");
                    var watch = Stopwatch.StartNew();
                    // Preserve the production binding expression and its 180ms delay.
                    search.SetCurrentValue(TextBox.TextProperty, term);
                    Require(ReferenceEquals(inputBinding, BindingOperations.GetBindingExpression(search, TextBox.TextProperty)),
                        "Programmatic input replaced the production search binding");
                    while (vm.SearchQuery != term)
                    {
                        Require(watch.Elapsed < TimeSpan.FromSeconds(5), "Search binding did not settle");
                        Wait(TimeSpan.FromMilliseconds(10));
                    }
                    Settle();
                    query.Add(new { term, milliseconds = watch.Elapsed.TotalMilliseconds, expected, actual = list.Items.Count });
                    Require(list.Items.Count == expected, "Search returned an incorrect deterministic count");
                    Checkpoint("query-filter-layout", new { repetition, term, bindingStatus = inputBinding!.Status.ToString() });
                    watch.Restart();
                    Volatile.Write(ref _phase, "query-filter-apply");
                    vm.ApplyFilter(); // Separately labelled filter+binding/layout, after debounce.
                    double synchronousFilterMs = watch.Elapsed.TotalMilliseconds;
                    Volatile.Write(ref _phase, "query-filter-idle-pump");
                    Settle();
                    queryPostDebounce.Add(watch.Elapsed.TotalMilliseconds);
                    Checkpoint("query-filter-layout-settled", new { repetition, term, synchronousFilterMs,
                        totalMs = watch.Elapsed.TotalMilliseconds, filteredItems = vm.FilteredClips.Count,
                        actualItems = list.Items.Count, root.IsMeasureValid, root.IsArrangeValid,
                        dispatcher = DispatcherDiagnostics() });
                    WaitForRowStills(list, expected, $"query-{repetition}-{term}");
                    containers.Add(CheckContainers(list, expected));
                }
                foreach (double position in new[] { 0d, .5, 1, .25, .75, 0, .9, 0 })
                {
                    Checkpoint("scroll-layout", new { repetition, position });
                    var watch = Stopwatch.StartNew();
                    scroll.ScrollToVerticalOffset(scroll.ScrollableHeight * position);
                    Settle();
                    scrollMs.Add(watch.Elapsed.TotalMilliseconds);
                    WaitForRowStills(list, rowCount, $"scroll-{repetition}-{position}");
                    scrollAndStillsReadyMs.Add(watch.Elapsed.TotalMilliseconds);
                    containers.Add(CheckContainers(list, rowCount));
                }
                Checkpoint("layout-invalidation", new { repetition });
                var layoutWatch = Stopwatch.StartNew();
                root.InvalidateMeasure(); root.UpdateLayout(); Settle();
                layoutMs.Add(layoutWatch.Elapsed.TotalMilliseconds);
                settingsMs.Add(OpenSettings(host, root, settings));
            }
            // Let bounded transitions/JIT settle after scripted operations. Samples
            // cover this inert-host synthetic idle, not normal app polling/gameplay.
            Wait(TimeSpan.FromSeconds(3));
            WaitForRowStills(list, rowCount, "pre-idle");
            stillObservations.Add(ObserveRowStills(list, rowCount, "before-idle-sampling"));
            Checkpoint("idle-sampling-begin", new { sampleSeconds });
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
            Checkpoint("idle-sampling-complete", new { count = resources.Count });
            Require(!main.IsLoaded && vm.SelectedClip == null && vm.CaptureHost.HostProcessId == null &&
                TimersStopped(vm) && TimersStopped(main) && !Descendants<MediaElement>(root).Any(media => media.Source != null),
                "Benchmark isolation changed during measurement");
            Require(clips.Count == rowCount && vm.FilteredClips.Count == rowCount, "Fixture changed during benchmark");
            stillObservations.Add(ObserveRowStills(list, rowCount, "after-idle-sampling"));
            var dpi = VisualTreeHelper.GetDpi(host);
            Checkpoint("serialize-measurements");
            resultJson = JsonSerializer.Serialize(new
            {
                schemaVersion = 1, status = "passed", evidenceClass = sampleSeconds < 300
                    ? "synthetic-short-CI-diagnostic" : "synthetic-five-minute-protocol",
                variant, revision, rowCount, sampleSeconds, warmupSeconds = 6,
                searchBindingDelayMs = inputBinding!.ParentBinding.Delay, searchBindingPreserved =
                    ReferenceEquals(inputBinding, BindingOperations.GetBindingExpression(search, TextBox.TextProperty)),
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
            }, Json);
            Checkpoint("measurements-serialized");
        }
        catch (Exception error)
        {
            failure = error;
            RecordFailure(error, "benchmark-exception");
        }
        finally
        {
            // Failure evidence is durable before cleanup. A faulted image/dispatcher
            // must not turn a 10-second readiness error into a 165-second mystery.
            using var cleanupWatchdog = new System.Threading.Timer(_ =>
            {
                if (Volatile.Read(ref _cleanupFinished) == 0) FatalBudget("cleanup-budget-exceeded");
            }, null, TimeSpan.FromSeconds(8), Timeout.InfiniteTimeSpan);
            try
            {
                Checkpoint("cleanup-detach-host");
                if (host != null) host.Content = null;
                Checkpoint("cleanup-close-host");
                host?.Close();
                Checkpoint("cleanup-dispose-viewmodel");
                vm?.Dispose();
                Checkpoint("cleanup-shutdown-application");
                app?.Shutdown();
                Checkpoint("cleanup-delete-owned-profile");
                if (profileRoot != null && marker != null && File.Exists(marker) && File.ReadAllText(marker) == token)
                    Directory.Delete(profileRoot, true);
                Checkpoint("cleanup-complete");
            }
            catch (Exception cleanupError)
            {
                failure ??= cleanupError;
                RecordFailure(cleanupError, "cleanup-exception");
            }
            finally { Interlocked.Exchange(ref _cleanupFinished, 1); }
        }
        if (failure == null)
        {
            try
            {
                Require(output != null && resultJson != null, "Measurement result was not prepared");
                ThrowBackgroundFailure();
                File.WriteAllText(output!, resultJson!); // A passing raw result requires successful cleanup too.
                Checkpoint("process-complete");
            }
            catch (Exception error) { failure = error; RecordFailure(error, "result-write-exception"); }
        }
        deadlineWatchdog?.Dispose();
        return failure == null ? 0 : 1;
    }

    private static void Checkpoint(string phase, object? details = null)
    {
        Volatile.Write(ref _phase, phase);
        try
        {
            string line = JsonSerializer.Serialize(new { timestampUtc = DateTimeOffset.UtcNow,
                elapsedSeconds = Lifetime.Elapsed.TotalSeconds, phase, thread = Environment.CurrentManagedThreadId, details });
            lock (TraceGate)
                if (_tracePath != null) File.AppendAllText(_tracePath, line + Environment.NewLine);
            Console.WriteLine($"Benchmark phase: {phase}");
        }
        catch (Exception error) { Console.Error.WriteLine("Checkpoint write failed: " + error); }
    }

    private static void RecordFailure(Exception error, string kind)
    {
        string failedDuring = Volatile.Read(ref _phase);
        Checkpoint(kind, new { failedDuring, error = error.ToString() });
        Console.Error.WriteLine(error);
        try
        {
            lock (TraceGate)
                if (_outputPath != null && !File.Exists(_outputPath))
                    File.WriteAllText(_outputPath, JsonSerializer.Serialize(new
                    {
                        schemaVersion = 1, status = "failed", phase = failedDuring,
                        error = error.ToString(), trace = Path.GetFileName(_tracePath), dispatcher = DispatcherDiagnostics()
                    }, Json));
        }
        catch (Exception writeError) { Console.Error.WriteLine("Failure evidence write failed: " + writeError); }
    }

    private static void FatalBudget(string reason)
    {
        try
        {
            RecordFailure(new TimeoutException(reason + "; last phase=" + Volatile.Read(ref _phase) +
                ". The owned benchmark process exits; any marker-owned profile is retained on this disposable runner."), reason);
        }
        finally { Environment.Exit(1); }
    }

    private static void ThrowBackgroundFailure()
    {
        if (Volatile.Read(ref _backgroundFailure) is { } error)
            throw new InvalidOperationException("An unobserved background operation faulted in the synthetic process", error);
    }

    private static object RowStillDiagnostics(ListBox list, int expectedItems)
    {
        var type = typeof(MainWindow).Assembly.GetType("LocalClip.App.Services.AsyncThumbnail");
        var stateProperty = type?.GetField("StateProperty", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null) as DependencyProperty;
        var loader = RowStillLoader();
        var images = RowImages(list);
        return new
        {
            expectedItems, actualItems = list.Items.Count, realizedContainers = Descendants<ListBoxItem>(list).Count(),
            rowImages = images.Length, pendingRequests = PendingRowStills(),
            cacheEntries = loader?.GetType().GetProperty("CacheEntryCount")?.GetValue(loader),
            visibleConsumers = type?.GetField("_visibleConsumers", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null),
            rows = images.Take(ContainerLimit + 1).Select(image =>
            {
                var clip = (ClipMetadata)image.DataContext;
                var bitmap = image.Source as BitmapSource;
                var state = stateProperty == null ? null : image.GetValue(stateProperty);
                var cancellation = state?.GetType().GetField("Cancellation")?.GetValue(state) as CancellationTokenSource;
                return new { clipId = clip.Id, file = Path.GetFileName(clip.ThumbnailPath), image.IsLoaded, image.IsVisible,
                    sourceType = image.Source?.GetType().FullName, pixelWidth = bitmap?.PixelWidth, pixelHeight = bitmap?.PixelHeight,
                    stateGeneration = state?.GetType().GetField("Generation")?.GetValue(state),
                    visibleConsumer = state?.GetType().GetField("VisibleConsumer")?.GetValue(state),
                    cancellationRequested = cancellation?.IsCancellationRequested };
            }).ToArray()
        };
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

    private static void WaitForRowStills(ListBox list, int items, string phase = "row-stills")
    {
        // The parent phase checkpoint is already durable. Update the watchdog
        // phase without adding file/console IO inside measured readiness timing.
        Volatile.Write(ref _phase, phase + "-stills-wait");
        var watch = Stopwatch.StartNew();
        while (true)
        {
            Settle();
            var images = RowImages(list);
            if ((items == 0 || images.Length > 0) && images.All(image =>
                image.Source is BitmapSource { PixelWidth: 256, PixelHeight: 144 }) && (PendingRowStills() ?? 0) == 0)
            {
                Volatile.Write(ref _phase, phase + "-stills-ready");
                return;
            }
            if (watch.Elapsed >= TimeSpan.FromSeconds(10))
            {
                Checkpoint(phase + "-stills-timeout", RowStillDiagnostics(list, items));
                throw new InvalidOperationException("Production row still decoding did not settle during " + phase);
            }
            Wait(TimeSpan.FromMilliseconds(10));
        }
    }

    private static object ObserveRowStills(ListBox list, int items, string phase)
    {
        WaitForRowStills(list, items, phase);
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
        Checkpoint("settings-construct");
        var watch = Stopwatch.StartNew();
        var page = new SettingsWindow(settings, () => false,
            loadAudioSources: () => Task.FromResult(new TargetList()),
            loadAudioEndpoints: _ => Array.Empty<AudioEndpoint>(),
            saveReviewedSettings: _ => throw new InvalidOperationException("Benchmark must not save preferences"),
            applyWindowsStartup: (_, _) => throw new InvalidOperationException("Benchmark must not write Windows startup"),
            readWindowsStartupStatus: _ => new WindowsStartupStatus(WindowsStartupRegistrationState.Registered, "Synthetic startup disabled"));
        Volatile.Write(ref _phase, "settings-show");
        host.Content = page;
        Settle();
        double milliseconds = watch.Elapsed.TotalMilliseconds;
        Checkpoint("settings-shown", new { milliseconds });
        Checkpoint("settings-leave");
        page.LeavePage();
        host.Content = workspace;
        Settle();
        Checkpoint("settings-workspace-restored");
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
        ThrowBackgroundFailure();
        var dispatcher = Dispatcher.CurrentDispatcher;
        var frame = new DispatcherFrame();
        int passes = 0;
        var pending = new List<DispatcherOperation>();
        // Preserve both natural ContextIdle turns. A Send-priority guard must
        // still be able to report higher-priority starvation instead of hanging
        // indefinitely inside a synchronous ContextIdle Invoke.
        Action? idle = null;
        idle = () =>
        {
            if (++passes == 2) frame.Continue = false;
            else pending.Add(dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, idle!));
        };
        pending.Add(dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, idle));
        var guard = new DispatcherTimer(DispatcherPriority.Send, dispatcher) { Interval = TimeSpan.FromSeconds(3) };
        guard.Tick += (_, _) => frame.Continue = false;
        guard.Start();
        try { Dispatcher.PushFrame(frame); }
        finally
        {
            guard.Stop();
            foreach (var operation in pending)
                if (operation.Status == DispatcherOperationStatus.Pending) operation.Abort();
        }
        if (passes != 2)
        {
            Checkpoint("dispatcher-settle-timeout", new { failedDuring = Volatile.Read(ref _phase), passes,
                dispatcher = DispatcherDiagnostics() });
            throw new TimeoutException("The real dispatcher did not complete two ContextIdle turns within three seconds.");
        }
        ThrowBackgroundFailure();
    }

    private static object DispatcherDiagnostics() => new
    {
        started = Interlocked.Read(ref _dispatcherStarted), completed = Interlocked.Read(ref _dispatcherCompleted),
        lastStartedPriority = ((DispatcherPriority)Volatile.Read(ref _lastDispatcherPriority)).ToString(),
        // The watchdog reads only captured primitive values, never UI objects.
        active = ActiveDispatcherOperations.Select(entry => new { priority = ((DispatcherPriority)entry.Value.Priority).ToString(),
            elapsedMilliseconds = Stopwatch.GetElapsedTime(entry.Value.Started).TotalMilliseconds }).ToArray()
    };

    private static void Wait(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero) return;
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = duration };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        try { Dispatcher.PushFrame(frame); }
        finally { timer.Stop(); }
        ThrowBackgroundFailure();
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
