using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LocalClip.App;
using LocalClip.App.ViewModels;
using LocalClip.App.Views.Components;
using LocalClip.Core.Models;
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
        foreach (string dictionary in new[] { "Colors", "Metrics", "Typography", "Controls", "Chrome" })
            app.Resources.MergedDictionaries.Add(new ResourceDictionary {
                Source = new Uri($"/Flick;component/Resources/Theme/Flick.{dictionary}.xaml", UriKind.Relative)
            });
        var main = new MainWindow();
        var vm = (MainViewModel)main.DataContext;
        T Find<T>(string name) => (T)main.FindName(name);
        void Invoke(string method, params object[] parameters) =>
            typeof(MainWindow).GetMethod(method, Private)!.Invoke(main, parameters);
        // Do not show the Window: Loaded would discover recording targets. Render
        // its actual WPF client visual directly, without starting a host/decoder.
        var root = (FrameworkElement)main.Content;
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
                    new AudioTrackInfo { Name = "Microphone", StreamIndex = 1, Gain = .7 }
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
        string applicationVersion = typeof(MainWindow).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
        File.WriteAllText(Path.Combine(output, "PROVENANCE.txt"),
            "Actual WPF client-area rendering from Flick MainWindow and resources.\n" +
            "Application informational version: " + applicationVersion + "\n" +
            "Application and renderer commit: " + Environment.GetEnvironmentVariable("GITHUB_SHA") + "\n" +
            "Workspace/settings: 1240x720 DIPs, 96 DPI. Synthetic clip names/thumbnails/monitor only.\n" +
            "Brand samples: actual shared Image style rasterized by WPF at 96/120/144/192 DPI; not a physical per-monitor DPI session.\n" +
            "No gameplay, media decoder, OS titlebar, real recording, updater network request, or full UI acceptance claimed.\n");
        vm.Dispose();
        app.Shutdown();
        return 0;

        void Render(string file, int width, int height)
        {
            root.Measure(new Size(width, height));
            root.Arrange(new Rect(0, 0, width, height));
            root.UpdateLayout();
            // Drain bindings/layout only; the unshown main Window never runs Loaded.
            root.Dispatcher.Invoke(() => { }, DispatcherPriority.Render);
            root.UpdateLayout();
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(root);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(Path.Combine(output, file));
            encoder.Save(stream);
            Console.WriteLine("Rendered " + file);
        }
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
            context.DrawRectangle(new SolidColorBrush(Color.FromRgb(36, 36, 54)), null, new Rect(0, 0, 256, 144));
            var pen = new Pen(new SolidColorBrush(Color.FromRgb(198, 155, 255)), 3);
            context.DrawRectangle(null, pen, new Rect(103, 52, 50, 40));
            for (int i = 0; i < 4; i++) context.DrawLine(pen, new Point(103 + i * 16, 52), new Point(103 + i * 16, 92));
        }
        var bitmap = new RenderTargetBitmap(256, 144, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(drawing);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }
}
