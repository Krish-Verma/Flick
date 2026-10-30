using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LocalClip.App.Services;

namespace Flick.UiRender;

internal static partial class Program
{
    private static void RenderHoverFixtures(Application app, string output)
    {
        var apply = typeof(FlickHoverFeedback).GetMethod("ApplyFeedback", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Missing production hover rendering operation.");
        var observations = new List<object>();
        foreach (var state in new[] { "rest", "hover", "disabled", "reduced-motion", "high-contrast-feedback" })
        {
            var canvas = new Grid { Width = 960, Height = 760, Background = (System.Windows.Media.Brush)app.FindResource("Flick.Brush.Canvas") };
            canvas.RowDefinitions.Add(new RowDefinition { Height = new GridLength(70) });
            canvas.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            var title = new TextBlock { Text = "Interaction states · " + state.Replace('-', ' '), Margin = new Thickness(28, 22, 28, 0),
                Style = (Style)app.FindResource("Flick.Text.PageTitle") };
            canvas.Children.Add(title);
            var grid = new UniformGrid { Columns = 2, Rows = 7, Margin = new Thickness(28, 0, 28, 24) };
            Grid.SetRow(grid, 1);
            canvas.Children.Add(grid);
            (string Label, Control Control)[] controls =
            [
                ("Primary action", new Button { Content = "Save settings", Style = (Style)app.FindResource("Flick.Button.Primary") }),
                ("Secondary action", new Button { Content = "Copy file", Style = (Style)app.FindResource("Flick.Button.Secondary") }),
                ("Quiet action", new Button { Content = "Open folder", Style = (Style)app.FindResource("Flick.Button.Ghost") }),
                ("Text field", new TextBox { Text = "Overtime finish", Style = (Style)app.FindResource("Flick.Input.Text") }),
                ("Password field", new PasswordBox { Password = "Synthetic fixture", Style = (Style)app.FindResource("Flick.Input.Password") }),
                ("Dropdown", new ComboBox { ItemsSource = new[] { "Original shape", "Portrait", "Square" }, SelectedIndex = 0, Style = (Style)app.FindResource("Flick.Input.Combo") }),
                ("Dropdown option", new ComboBoxItem { Content = "Original shape", Style = (Style)app.FindResource("Flick.Input.ComboItem") }),
                ("Settings toggle", new CheckBox { Content = "Keep replay ready in the tray", IsChecked = true, Style = (Style)app.FindResource("Flick.Toggle") }),
                ("Audio checkbox", new CheckBox { Content = "Mute", Style = (Style)app.FindResource("Flick.Input.CheckBox") }),
                ("Library filter", new RadioButton { Content = "All clips", IsChecked = true, Style = (Style)app.FindResource("Flick.Library.FilterChip") }),
                ("Library row", new ListBoxItem { Content = "Overtime finish", Style = (Style)app.FindResource("Flick.Library.Item") }),
                ("Playback / audio slider", new Slider { Value = 65, Minimum = 0, Maximum = 100, Style = (Style)app.FindResource("Flick.Input.Slider") }),
                ("Scrollbar grip", new Thumb { Width = 100, Height = 12, HorizontalAlignment = HorizontalAlignment.Left, Style = (Style)app.FindResource("Flick.Scrollbar.Thumb") }),
                ("Menu action (no adorner layer)", new MenuItem { Header = "Open original folder", Style = (Style)app.FindResource(typeof(MenuItem)) })
            ];
            foreach (var (label, control) in controls)
            {
                var group = new StackPanel { Margin = new Thickness(0, 0, 28, 10) };
                group.Children.Add(new TextBlock { Text = label, Style = (Style)app.FindResource("Flick.Text.Caption"), Margin = new Thickness(0, 0, 0, 6) });
                group.Children.Add(control);
                grid.Children.Add(group);
            }
            canvas.Measure(new Size(960, 760));
            canvas.Arrange(new Rect(0, 0, 960, 760));
            canvas.UpdateLayout();
            canvas.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            foreach (var (label, control) in controls)
            {
                control.IsEnabled = state != "disabled";
                // Use the production operation deterministically without moving
                // the OS cursor or showing a Window/recording consent surface.
                apply.Invoke(null, [control, state != "rest", false, state == "high-contrast-feedback"]);
                var surface = control.Template.FindName(FlickHoverFeedback.SurfacePartName, control) as Border
                    ?? throw new InvalidOperationException("Missing feedback surface: " + label);
                if (surface.IsHitTestVisible || (state is "rest" or "disabled" ? surface.Opacity != 0 : surface.Opacity <= 0))
                    throw new InvalidOperationException("Incorrect feedback state: " + label + "/" + state);
                observations.Add(new { State = state, Control = label, SurfaceOpacity = surface.Opacity,
                    ControlOpacity = control.Opacity, SurfaceCannotInterceptInput = !surface.IsHitTestVisible,
                    NativeAdornerLayerRequired = false, NoCapturePerformed = true });
            }
            canvas.UpdateLayout();
            var bitmap = new RenderTargetBitmap(960, 760, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(canvas);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            int number = state switch { "rest" => 137, "hover" => 138, "disabled" => 139, "reduced-motion" => 140, _ => 141 };
            using var stream = File.Create(Path.Combine(output, $"{number}-controls-{state}.png"));
            encoder.Save(stream);
        }
        File.WriteAllText(Path.Combine(output, "hover-feedback-evidence.json"), JsonSerializer.Serialize(new {
            SyntheticControlStates = true, UsesProductionStylesAndFeedback = true,
            HighContrastFrameExercisesFeedbackOnly = true, NoOperatingSystemPreferencesChanged = true,
            Observations = observations
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
