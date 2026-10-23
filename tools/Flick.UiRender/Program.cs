using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LocalClip.App;
using LocalClip.App.ViewModels;
using LocalClip.Core.Models;

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
        foreach (string name in new[] { "Practice round", "Last round clutch", "Overtime finish" })
        {
            string source = Path.Combine(fixtures, name + ".mkv");
            File.WriteAllBytes(source, Array.Empty<byte>());
            string poster = Path.Combine(fixtures, name + ".png");
            WriteSyntheticPoster(poster);
            vm.FilteredClips.Add(new ClipMetadata {
                Title = name, OriginalFilePath = source, ThumbnailPath = poster,
                DurationSeconds = 30, TrimEndSeconds = 30,
                AudioTracks = new() {
                    new AudioTrackInfo { Name = "System Audio", StreamIndex = 0, Gain = 1 },
                    new AudioTrackInfo { Name = "Microphone", StreamIndex = 1, Gain = .7 }
                }
            });
        }
        vm.SelectedClip = vm.FilteredClips[1];
        Invoke("ResetPlayerForSelection");
        // Keep an honest empty preview instead of substituting fabricated gameplay.
        Find<Image>("PlayerPoster").Visibility = Visibility.Collapsed;
        Find<TextBlock>("PlayerPosterHint").Text = "Press Play to Preview";
        Find<ComboBox>("CmbTargets").ItemsSource = new[] { new TargetItem { DisplayName = "Choose a window or monitor" } };
        Find<ComboBox>("CmbTargets").SelectedIndex = 0;
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
        File.WriteAllText(Path.Combine(output, "PROVENANCE.txt"),
            "Actual WPF client-area rendering from Flick MainWindow and resources.\n" +
            "Application source base: 79204f67ca9318526873730946d82556eee9ce9c\n" +
            "Renderer commit: " + Environment.GetEnvironmentVariable("GITHUB_SHA") + "\n" +
            "1240x720 DIPs, 96 DPI. Synthetic clip names/thumbnails only; no gameplay, media decoder, OS titlebar, real recording, or full UI acceptance claimed.\n");
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
