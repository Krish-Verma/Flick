using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LocalClip.App;
using LocalClip.App.ViewModels;
using LocalClip.App.Views.Components;
using LocalClip.Core.Diagnostics;
using LocalClip.Core.Ipc;
using LocalClip.Core.Models;
using LocalClip.Core.Rules;
using LocalClip.Core.Storage;

namespace Flick.UiRender;

internal static class Program
{
    private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [STAThread]
    private static int Main(string[] args)
    {
        if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true")
            throw new InvalidOperationException("This synthetic renderer runs only in an isolated GitHub Actions job.");
        string output = Path.GetFullPath(args.Length == 0 ? "ui-render" : args[0]);
        Directory.CreateDirectory(output);
        string fixtures = Path.Combine(Path.GetTempPath(), "Flick-UiRender-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixtures);
        string profile = AppSettings.GetSettingsFilePath();
        if (File.Exists(profile)) throw new InvalidOperationException("Refusing to replace an existing Flick profile.");
        var settings = AppSettings.CreateDefault();
        settings.StorageRoot = fixtures;
        settings.OriginalsFolder = Path.Combine(fixtures, "Originals");
        settings.ExportsFolder = Path.Combine(fixtures, "Exports");
        settings.TempFolder = Path.Combine(fixtures, "Temp");
        settings.SaveToFile(profile);

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        foreach (string dictionary in new[] { "Colors", "Metrics", "Typography", "Icons", "Controls", "Chrome" })
            app.Resources.MergedDictionaries.Add(new ResourceDictionary {
                Source = new Uri($"/Flick;component/Resources/Theme/Flick.{dictionary}.xaml", UriKind.Relative)
            });
        var main = new MainWindow();
        var vm = (MainViewModel)main.DataContext;
        // An unshown Window still constructs the VM's timer. Synthetic health
        // observations must not be replaced by a real no-host polling result.
        ((DispatcherTimer)typeof(MainViewModel).GetField("_pollTimer", Private)!.GetValue(vm)!).Stop();
        T Find<T>(string name) => (T)main.FindName(name);
        void Invoke(string method, params object[] parameters) =>
            typeof(MainWindow).GetMethod(method, Private)!.Invoke(main, parameters);
        // Do not show the Window: Loaded would discover recording targets. Render
        // its actual WPF client visual directly, without starting a host/decoder.
        var root = (FrameworkElement)main.Content;
        var layoutObservations = new List<object>();
        // The Window normally paints this underneath its content. Rendering
        // Content alone otherwise leaves transparent/black gaps around panels.
        if (root is Panel panel) panel.Background = main.Background;
        var storage = (ClipStorage)typeof(MainViewModel).GetField("_storage", Private)!.GetValue(vm)!;
        foreach (string name in new[] { "Practice round", "Last round clutch", "Overtime finish" })
        {
            string source = Path.Combine(fixtures, name + ".mkv");
            File.WriteAllBytes(source, Array.Empty<byte>());
            string poster = Path.Combine(fixtures, name + ".png");
            WriteSyntheticPoster(poster);
            storage.AddOrUpdateClip(new ClipMetadata {
                Title = name, OriginalFilePath = source, ThumbnailPath = poster,
                DurationSeconds = 30, TrimEndSeconds = 30,
                AudioTracks = new() {
                    new AudioTrackInfo { Name = "System Audio", StreamIndex = 0, Gain = 1 },
                    // Show both checkbox states in the real audio-panel render.
                    new AudioTrackInfo { Name = "Microphone", StreamIndex = 1, Gain = .7, IsMuted = true }
                }
            });
        }
        vm.ReloadClips();
        vm.SelectedClip = vm.FilteredClips[1];
        Invoke("ResetPlayerForSelection");
        // Keep an honest empty preview instead of substituting fabricated gameplay.
        Find<Image>("PlayerPoster").Visibility = Visibility.Collapsed;
        Find<TextBlock>("PlayerPosterHint").Text = "Press Play to Preview";
        // Populate the real bound collection/selection together. A local
        // ComboBox selection is overwritten by the VM's null selected target.
        vm.Targets.Add(new TargetItem { DisplayName = "Sample monitor (synthetic)", Type = "Monitor" });
        vm.SelectedTarget = vm.Targets[0];
        Invoke("UpdateStatusBadgeColor");
        Find<RadioButton>("RadioAllClips").IsChecked = true;
        Invoke("ApplyWorkspaceLayout");
        Render("01-workspace.png", 1240, 720);
        Invoke("EditorTool_Click", Find<Button>("ToolTrim"), new RoutedEventArgs());
        Render("02-trim.png", 1240, 720);
        Invoke("EditorTool_Click", Find<Button>("ToolAudio"), new RoutedEventArgs());
        Render("03-audio.png", 1240, 720);
        Invoke("EditorTool_Click", Find<Button>("ToolShare"), new RoutedEventArgs());
        Render("04-share.png", 1240, 720);
        Invoke("OpenSettings");
        Render("05-settings.png", 1240, 720);
        var settingsPage = (FrameworkElement)typeof(MainWindow).GetField("_settingsPage", Private)!.GetValue(main)!;
        settingsPage.GetType().GetMethod("SelectCategory", Private)!.Invoke(settingsPage, new object[] { "General" });
        Render("06-settings-general.png", 1240, 720);
        // This is the same live General page scrolled to its update section,
        // without invoking the check/download buttons or inventing a result.
        var versionText = (FrameworkElement)settingsPage.FindName("TxtCurrentVersion");
        var generalPanel = (FrameworkElement)settingsPage.FindName("PanelGeneral");
        var settingsScroll = (ScrollViewer)settingsPage.FindName("SettingsScroll");
        settingsScroll.ScrollToVerticalOffset(versionText.TranslatePoint(new Point(0, -35), generalPanel).Y);
        Render("07-settings-updates.png", 1240, 720);
        RenderBrandScaleSamples(app, output);
        RenderSavedToast(output);
        RenderNativeTrayIcon(main, output);
        Invoke("ReturnToClipWorkspace");
        Invoke("CloseEditorContext");
        // Navigation restores the synthetic thumbnail as a poster. Keep the
        // same honest empty preview fixture across all viewport captures.
        Find<Image>("PlayerPoster").Visibility = Visibility.Collapsed;
        Render("11-workspace-wide.png", 1440, 900);
        // Exercise the existing compact library/editor branches without showing
        // a Window or synthesizing new layout rules in this renderer.
        typeof(MainWindow).GetField("_layoutMode", Private)!.SetValue(main, WorkspaceLayoutMode.Compact);
        Invoke("ShowCompactLibrary");
        Invoke("UpdateStatusBadgeColor");
        Render("12-library-compact.png", 800, 600);
        Invoke("ShowCompactEditor");
        Render("13-editor-compact.png", 800, 600);
        // Exercise the actual compact Share controls without exporting a file
        // or contacting Discord. Extra options must fit the smaller panel too.
        Invoke("EditorTool_Click", Find<Button>("ToolShare"), new RoutedEventArgs());
        Render("14-share-compact.png", 800, 600);
        Find<ComboBox>("CmbExportCap").SelectedIndex = 4;
        Find<TextBox>("TxtCustomMb").Text = "250";
        Render("15-share-custom-compact.png", 800, 600);
        Find<Expander>("AdvancedShareExpander").IsExpanded = true;
        Render("16-share-expanded-compact.png", 800, 600);
        Invoke("CloseEditorContext");
        vm.CaptureStatus = "Armed";
        vm.BufferedDurationText = "~30.0s / 30s";
        vm.StatusDetails = "Ready to save retained replay.";
        typeof(MainViewModel).GetMethod("SetCaptureProfile", Private)!.Invoke(vm, new object[] {
            new CaptureEncodingProfile("obs_qsv11_v2", "Intel Quick Sync H.264", false, 1920, 1080, 60)
        });
        Invoke("UpdateStatusBadgeColor");
        Render("17-hardware-status-compact.png", 800, 600);
        typeof(MainViewModel).GetMethod("SetCaptureProfile", Private)!.Invoke(vm, new object[] {
            new CaptureEncodingProfile("obs_x264", "CPU x264 H.264", true, 1280, 720, 30)
        });
        Render("18-cpu-warning-compact.png", 800, 600);
        Render("19-cpu-warning-wide.png", 1240, 720);
        // A1 health fixtures drive the real ledger/bindings, never a capture
        // helper or a media validator. Success below is a simulated health
        // observation; none of the placeholder MKVs is treated as valid media.
        var health = (RecordingHealthLedger)typeof(MainViewModel).GetField("_health", Private)!.GetValue(vm)!;
        void PublishHealth()
        {
            typeof(MainViewModel).GetMethod("PublishHealth", Private)!.Invoke(vm, null);
            Invoke("UpdateStatusBadgeColor");
        }
        Guid BeginHealthFixture(string source)
        {
            Guid session = health.BeginSession(new RecordingSource("Monitor", source));
            typeof(MainViewModel).GetField("_healthSession", Private)!.SetValue(vm, session);
            vm.CaptureStatus = "Armed";
            typeof(MainViewModel).GetMethod("SetCaptureProfile", Private)!.Invoke(vm, new object[] {
                new CaptureEncodingProfile("obs_qsv11_v2", "Intel Quick Sync H.264", false, 1920, 1080, 60)
            });
            health.ObserveHost(session, CaptureHostHealth.Responsive);
            health.ObserveBuffer(session, 30, 30, estimated: false);
            vm.BufferedDurationText = "30.0s / 30s";
            return session;
        }
        void RenderHealthPair(string wide, string compact)
        {
            typeof(MainWindow).GetField("_layoutMode", Private)!.SetValue(main, WorkspaceLayoutMode.Split);
            Invoke("ApplyWorkspaceLayout");
            PublishHealth();
            Invoke("UpdateStatusBadgeColor");
            Render(wide, 1240, 720);
            typeof(MainWindow).GetField("_layoutMode", Private)!.SetValue(main, WorkspaceLayoutMode.Compact);
            Invoke("ShowCompactEditor");
            PublishHealth();
            Invoke("UpdateStatusBadgeColor");
            Render(compact, 800, 600);
        }
        vm.ReportHotkeyHealth("Toggle replay", settings.CaptureToggleHotkey,
            settings.CaptureToggleHotkey, "Synthetic registration fixture");
        vm.ReportHotkeyHealth("Save replay", settings.SaveReplayHotkey, null,
            "Synthetic conflict: choose another shortcut in Settings.");
        Guid faultSession = BeginHealthFixture("Sample display (synthetic)");
        health.BeginSave(faultSession, "render-failed-save");
        health.RecordSaveFailure(faultSession, "render-failed-save", "fixture-save-failed",
            "Simulated: the originals folder is unavailable. Choose an accessible folder in Settings and try again.");
        health.RecordRecordingIssue(faultSession, "fixture-target-lost",
            "Simulated: the selected window closed. Choose an available source and start replay again.");
        vm.CaptureStatus = "Faulted";
        vm.StatusDetails = "Synthetic recording-health fixture · no capture performed";
        Invoke("BtnRecordingHealth_Click", Find<Button>("BtnRecordingHealth"), new RoutedEventArgs());
        RenderHealthPair("20-health-fault-wide.png", "21-health-fault-compact.png");

        Guid saveSession = BeginHealthFixture("Second sample display (synthetic)");
        health.BeginSave(saveSession, "render-validating-save");
        health.AcknowledgeSave(saveSession, "render-validating-save", true);
        health.MarkSaveValidating(saveSession, "render-validating-save");
        RenderHealthPair("22-health-validating-wide.png", "23-health-validating-compact.png");
        health.RecordValidatedClip(saveSession, "render-validating-save", "synthetic-health-summary-only", 18.5);
        RenderHealthPair("24-health-success-wide.png", "25-health-success-compact.png");
        vm.CaptureStatus = "Off";
        health.ObserveEngine(saveSession, RecordingEngineState.Stopped);
        health.ObserveHost(saveSession, CaptureHostHealth.Exited);
        PublishHealth();
        Render("26-health-stopped-retained-compact.png", 800, 600);

        // A2: only simulate bindable status/busy observations. Do not initialize
        // a diagnostics journal, open a save dialog, or export a bundle here.
        void SetDiagnosticsFixture(bool busy, string message)
        {
            typeof(MainViewModel).GetField("_isExportingDiagnostics", Private)!.SetValue(vm, busy);
            typeof(MainViewModel).GetField("_diagnosticStatusMessage", Private)!.SetValue(vm, message);
            foreach (string property in new[] { nameof(vm.DiagnosticStatusText), nameof(vm.CanExportDiagnostics) })
                typeof(MainViewModel).GetMethod("OnPropertyChanged", Private)!.Invoke(vm, new object[] { property });
            if (vm.DiagnosticsInitialized || typeof(MainViewModel).GetField("_diagnosticInitialization", Private)!.GetValue(vm) != null)
                throw new InvalidOperationException("Synthetic diagnostics renders must never initialize a journal.");
        }
        SetDiagnosticsFixture(false, "Synthetic fixture: local diagnostics have not been initialized.");
        typeof(MainWindow).GetField("_layoutMode", Private)!.SetValue(main, WorkspaceLayoutMode.Split);
        Invoke("ApplyWorkspaceLayout");
        PublishHealth();
        Render("27-diagnostics-wide.png", 1240, 720);

        Guid diagnosticsSession = BeginHealthFixture("Sample display (synthetic)");
        health.RecordRecordingIssue(diagnosticsSession, "fixture-long-error",
            "Simulated: the selected recording source became unavailable. Choose another available source and start replay again. " +
            "This deliberately long retained error exercises the scrolling health details at the minimum window size. " +
            "No capture device or diagnostics journal was opened by this fixture.");
        vm.CaptureStatus = "Faulted";
        SetDiagnosticsFixture(false,
            "Synthetic fixture: local diagnostics are unavailable. Recording can continue. Choose another export location and try again.");
        typeof(MainWindow).GetField("_layoutMode", Private)!.SetValue(main, WorkspaceLayoutMode.Compact);
        Invoke("ShowCompactEditor");
        PublishHealth();
        Find<ScrollViewer>("RecordingHealthScroll").ScrollToTop();
        Render("28-diagnostics-minimum-long-errors.png", 720, 480);

        SetDiagnosticsFixture(true, "Synthetic fixture: creating a local redacted diagnostics ZIP. No export is running.");
        Render("29-diagnostics-busy-compact.png", 800, 600);
        SetDiagnosticsFixture(false, "Synthetic fixture: no journal initialized; nothing was exported or uploaded.");
        Find<ScrollViewer>("RecordingHealthScroll").ScrollToBottom();
        Render("30-diagnostics-minimum-scrolled.png", 720, 480);
        string applicationVersion = typeof(MainWindow).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
        File.WriteAllText(Path.Combine(output, "PROVENANCE.txt"),
            "Actual WPF client-area rendering from Flick MainWindow and resources.\n" +
            "Application informational version: " + applicationVersion + "\n" +
            "Application and renderer commit: " + Environment.GetEnvironmentVariable("GITHUB_SHA") + "\n" +
            "Workspace/settings: 1240x720 DIPs, 96 DPI. Synthetic clip names/thumbnails/monitor only.\n" +
            "Additional views: 1440x900 workspace; existing 800x600 compact library and editor branches.\n" +
            "Compact Share: default, Custom 250 MB and expanded optional posting; no export or post action invoked.\n" +
            "Encoder status: synthetic Intel/CPU profiles exercise real bindings and the persistent compatibility warning. No encoder or capture host started.\n" +
            "Recording health: synthetic ledger observations exercise actual fault/retained-error, validating, success and stopped bindings at wide/compact sizes. Hotkey outcomes are simulated; no hotkeys registered and no clip validated by this renderer.\n" +
            "The VM polling timer is stopped for fixture isolation. Binding/render dispatcher work and recursive visual-tree measure invalidation settle changed labels before rasterization; no control widths or pixels are substituted. layout-observations.json records actual Save button geometry and an unconstrained caption measurement; clipped captions fail this renderer.\n" +
            "The health disclosure is rendered as part of the actual same-window WPF visual; native focus, keyboard and physical placement acceptance are separate.\n" +
            "Diagnostics views simulate only status/busy fields through actual bindings, including long errors at720x480DIPs. No diagnostics journal is initialized, no save dialog opens, and no ZIP is exported. Fixed header/footer/Close/Export containment and the finite middle scroll viewport are checked in layout-observations.json.\n" +
            "Brand samples: actual shared Image style rasterized by WPF at 96/120/144/192 DPI; not a physical per-monitor DPI session.\n" +
            "No gameplay, media decoder, OS titlebar, real recording, updater network request, or full UI acceptance claimed.\n");
        vm.Dispose();
        app.Shutdown();
        return 0;

        void Render(string file, int width, int height)
        {
            var viewport = new Size(width, height);
            var bounds = new Rect(0, 0, width, height);
            void SettleLayout()
            {
                for (int pass = 0; pass < 3; pass++)
                {
                    root.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
                    InvalidateVisualMeasure(root);
                    root.Measure(viewport);
                    root.Arrange(bounds);
                    root.UpdateLayout();
                    root.Dispatcher.Invoke(() => { }, DispatcherPriority.Render);
                    root.UpdateLayout();
                }
            }
            // A live Window schedules another measure after binding/template
            // changes. This detached client visual must explicitly repeat the
            // same work when compact/wide labels change their desired size.
            // Drain bindings/layout only; the unshown Window never runs Loaded.
            SettleLayout();
            var saveButton = Find<Button>("BtnSaveReplay");
            var presenter = FindVisualDescendant<ContentPresenter>(saveButton)
                ?? throw new InvalidOperationException("The real Save button has no content presenter.");
            // Measure the real caption unconstrained, then restore the entire
            // actual viewport layout. This observes its required size without
            // assigning a width or substituting another rendered control.
            presenter.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double unconstrainedPresenterWidth = presenter.DesiredSize.Width;
            double captionWidth = Math.Max(0, unconstrainedPresenterWidth -
                presenter.Margin.Left - presenter.Margin.Right);
            SettleLayout();
            double requiredWidth = captionWidth + saveButton.Padding.Left + saveButton.Padding.Right +
                saveButton.BorderThickness.Left + saveButton.BorderThickness.Right;
            double roundingTolerance = 0.5 / VisualTreeHelper.GetDpi(saveButton).DpiScaleX;
            bool captionFits = saveButton.ActualWidth + roundingTolerance >= requiredWidth &&
                presenter.ActualWidth + roundingTolerance >= captionWidth;
            object? healthLayout = null;
            string? healthLayoutFailure = null;
            var healthPanel = Find<Border>("RecordingHealthPanel");
            if (healthPanel.Visibility == Visibility.Visible)
            {
                Rect Bounds(FrameworkElement element) => element.TransformToAncestor(root)
                    .TransformBounds(new Rect(element.RenderSize));
                bool Contains(Rect outer, Rect inner) => inner.Left >= outer.Left - roundingTolerance &&
                    inner.Top >= outer.Top - roundingTolerance && inner.Right <= outer.Right + roundingTolerance &&
                    inner.Bottom <= outer.Bottom + roundingTolerance;
                object Coordinates(Rect bounds) => new { x = bounds.X, y = bounds.Y, width = bounds.Width, height = bounds.Height };
                var header = Find<Grid>("RecordingHealthHeader");
                var footer = Find<StackPanel>("RecordingDiagnosticsFooter");
                var close = Find<Button>("BtnCloseRecordingHealth");
                var export = Find<Button>("BtnExportDiagnostics");
                var scroll = Find<ScrollViewer>("RecordingHealthScroll");
                Rect panelBounds = Bounds(healthPanel), headerBounds = Bounds(header), footerBounds = Bounds(footer);
                Rect closeBounds = Bounds(close), exportBounds = Bounds(export), scrollBounds = Bounds(scroll);
                bool fixedControlsFit = Contains(new Rect(root.RenderSize), panelBounds) &&
                    Contains(panelBounds, headerBounds) && Contains(panelBounds, footerBounds) &&
                    Contains(headerBounds, closeBounds) && Contains(footerBounds, exportBounds) &&
                    headerBounds.Bottom <= scrollBounds.Top + roundingTolerance &&
                    scrollBounds.Bottom <= footerBounds.Top + roundingTolerance &&
                    double.IsFinite(scroll.ViewportHeight) && scroll.ViewportHeight > 0;
                healthLayout = new {
                    panel = Coordinates(panelBounds), header = Coordinates(headerBounds), footer = Coordinates(footerBounds),
                    close = Coordinates(closeBounds), export = Coordinates(exportBounds), scroll = Coordinates(scrollBounds),
                    scroll.ViewportHeight, scroll.ExtentHeight, scroll.ScrollableHeight, scroll.VerticalOffset,
                    diagnosticsInitialized = vm.DiagnosticsInitialized, exportEnabled = export.IsEnabled, fixedControlsFit
                };
                if (!fixedControlsFit)
                    healthLayoutFailure = $"Fixed diagnostics controls or scroll viewport do not fit in {file}.";
                if (file == "28-diagnostics-minimum-long-errors.png" && scroll.ScrollableHeight <= 0)
                    healthLayoutFailure = "The minimum-size diagnostics fixture must exercise overflowing health details.";
            }
            layoutObservations.Add(new
            {
                image = file,
                viewportWidth = width,
                viewportHeight = height,
                layoutMode = ((WorkspaceLayoutMode)typeof(MainWindow).GetField("_layoutMode", Private)!.GetValue(main)!).ToString(),
                saveCaption = Convert.ToString(saveButton.Content),
                saveDesiredWidth = saveButton.DesiredSize.Width,
                saveDesiredHeight = saveButton.DesiredSize.Height,
                saveActualWidth = saveButton.ActualWidth,
                contentPresenterDesiredWidth = presenter.DesiredSize.Width,
                contentPresenterActualWidth = presenter.ActualWidth,
                contentPresenterUnconstrainedDesiredWidth = unconstrainedPresenterWidth,
                captionUnconstrainedDesiredWidth = captionWidth,
                requiredButtonWidth = requiredWidth,
                roundingTolerance,
                captionFits,
                healthLayout
            });
            File.WriteAllText(Path.Combine(output, "layout-observations.json"),
                JsonSerializer.Serialize(layoutObservations, new JsonSerializerOptions { WriteIndented = true }));
            if (healthLayoutFailure != null) throw new InvalidOperationException(healthLayoutFailure);
            if (!captionFits)
                throw new InvalidOperationException($"Save caption is clipped in {file}: actual button width " +
                    $"{saveButton.ActualWidth:F2}, required {requiredWidth:F2}; presenter width " +
                    $"{presenter.ActualWidth:F2}, caption {captionWidth:F2}.");
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(root);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(Path.Combine(output, file));
            encoder.Save(stream);
            Console.WriteLine("Rendered " + file);
        }
    }

    private static void InvalidateVisualMeasure(DependencyObject visual)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(visual); i++)
            InvalidateVisualMeasure(VisualTreeHelper.GetChild(visual, i));
        if (visual is UIElement element) element.InvalidateMeasure();
    }

    private static T? FindVisualDescendant<T>(DependencyObject visual) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(visual); i++)
        {
            var child = VisualTreeHelper.GetChild(visual, i);
            if (child is T match) return match;
            if (FindVisualDescendant<T>(child) is { } nested) return nested;
        }
        return null;
    }

    private static void RenderBrandScaleSamples(Application app, string output)
    {
        foreach (int percent in new[] { 100, 125, 150, 200 })
        {
            double scale = percent / 100d;
            int pixels = (int)Math.Round(38 * scale);
            var mark = new Image { Style = (Style)app.FindResource("Flick.Image.BrandMark") };
            mark.Measure(new Size(38, 38));
            mark.Arrange(new Rect(0, 0, 38, 38));
            mark.UpdateLayout();
            var bitmap = new RenderTargetBitmap(pixels, pixels, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            bitmap.Render(mark);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(Path.Combine(output, $"08-logo-{percent}.png"));
            encoder.Save(stream);
            Console.WriteLine($"Rendered shared brand style at {percent}% rasterization");
        }
    }

    private static void RenderSavedToast(string output)
    {
        // Actual notification control with its real success defaults. Rendering
        // the control does not display a popup or play a notification sound.
        var toast = new FlickClipToast();
        toast.Measure(new Size(278, double.PositiveInfinity));
        int height = (int)Math.Ceiling(toast.DesiredSize.Height);
        toast.Arrange(new Rect(0, 0, 278, height));
        toast.UpdateLayout();
        toast.Dispatcher.Invoke(() => { }, DispatcherPriority.Render);
        var bitmap = new RenderTargetBitmap(278, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(toast);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(output, "09-clip-saved-toast.png"));
        encoder.Save(stream);
        Console.WriteLine("Rendered actual Clip Saved notification control");
    }

    private static void RenderNativeTrayIcon(MainWindow main, string output)
    {
        // Exercise the actual runtime native-icon conversion, without creating
        // a NotifyIcon or touching the Windows taskbar. Keep System.Drawing
        // types in the app assembly so this WPF-only harness stays WPF-only.
        // Invoke the production conversion core directly so a genuine resource
        // or GDI error is visible in CI. Normal app startup retains its fallback.
        object icon;
        try { icon = typeof(MainWindow).GetMethod("CreateTrayIconCore", Private)!.Invoke(main, null)!; }
        catch (TargetInvocationException error)
        { throw new InvalidOperationException("Native Flick icon conversion failed.", error.InnerException ?? error); }
        using var lifetime = (IDisposable)icon;
        var handle = (IntPtr)icon.GetType().GetProperty("Handle")!.GetValue(icon)!;
        var bitmap = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
            handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(output, "10-native-tray-icon.png"));
        encoder.Save(stream);
        Console.WriteLine("Rendered actual runtime native tray icon without showing it");
    }

    private static void WriteSyntheticPoster(string path)
    {
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen())
        {
            context.DrawRectangle((Brush)Application.Current.FindResource("Flick.Brush.Raised"), null, new Rect(0, 0, 256, 144));
            var pen = new Pen((Brush)Application.Current.FindResource("Flick.Brush.TextMuted"), 1.5);
            context.PushTransform(new TranslateTransform(108, 52));
            context.PushTransform(new ScaleTransform(2, 2));
            context.DrawGeometry(null, pen, (Geometry)Application.Current.FindResource("Flick.Icon.Geometry.Original"));
            context.Pop();
            context.Pop();
        }
        var bitmap = new RenderTargetBitmap(256, 144, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(drawing);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }
}
