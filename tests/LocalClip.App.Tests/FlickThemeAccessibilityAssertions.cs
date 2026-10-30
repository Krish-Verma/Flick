using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LocalClip.App.Services;
using Xunit;

namespace LocalClip.App.Tests;

/// <summary>
/// Uses the production resource dictionaries in the existing STA Application.
/// Checks actual resolved states and native automation patterns without changing
/// OS preferences, moving the pointer, opening media or claiming Narrator QA.
/// </summary>
internal static class FlickThemeAccessibilityAssertions
{
    public static void Run()
    {
        var app = Assert.IsAssignableFrom<Application>(Application.Current);
        Style Style(string key) => Assert.IsType<Style>(app.FindResource(key));
        Brush Brush(string key) => Assert.IsAssignableFrom<Brush>(app.FindResource(key));

        var controlText = new TextBlock { Style = Style("Flick.Text.Control"), Text = "Copy file" };
        var statusText = new TextBlock { Style = Style("Flick.Text.Status"), Text = "Checking saved clip…" };
        var numericText = new TextBlock { Style = Style("Flick.Text.Numeric"), Text = "0:30 · 18.4 MB" };
        Assert.Equal(14, controlText.FontSize);
        Assert.Equal(13, statusText.FontSize);
        Assert.Equal(13, numericText.FontSize);
        Assert.Equal(FontNumeralAlignment.Tabular, Typography.GetNumeralAlignment(numericText));
        Assert.Equal(FontNumeralStyle.Lining, Typography.GetNumeralStyle(numericText));
        Assert.Equal(new Thickness(2), Assert.IsType<Thickness>(app.FindResource("Flick.Thickness.Focus")));

        // Test all normal text roles against their supported surface layers,
        // including hover compositing. This catches palette and alpha regressions.
        string[] surfaces = ["Canvas", "Surface", "Raised", "Hover"];
        foreach (string surface in surfaces)
        {
            Color background = Assert.IsType<SolidColorBrush>(Brush("Flick.Brush." + surface)).Color;
            foreach (string role in new[] { "TextPrimary", "TextSecondary", "TextMuted", "Success", "Warning", "Danger" })
            {
                Color foreground = Assert.IsType<SolidColorBrush>(Brush("Flick.Brush." + role)).Color;
                Assert.True(Contrast(foreground, background) >= 4.5, $"{role} on {surface} must retain 4.5:1 text contrast");
            }
            Color focus = Assert.IsType<SolidColorBrush>(Brush("Flick.Brush.Focus")).Color;
            Assert.True(Contrast(focus, background) >= 3, $"Focus on {surface} must retain 3:1 contrast");
            Color strong = Assert.IsType<SolidColorBrush>(Brush("Flick.Brush.BorderStrong")).Color;
            Assert.True(Contrast(strong, background) >= 3, $"Control/selection boundary on {surface} must retain 3:1 contrast");
        }

        Color onAccent = Assert.IsType<SolidColorBrush>(Brush("Flick.Brush.OnAccent")).Color;
        foreach (string key in new[] { "Flick.Brush.PrimaryGradient", "Flick.Brush.PrimaryGradientHover" })
        {
            var gradient = Assert.IsType<LinearGradientBrush>(Brush(key));
            Assert.Equal(2, gradient.GradientStops.Count);
            for (int sample = 0; sample <= 20; sample++)
            {
                Color background = Blend(gradient.GradientStops[0].Color, gradient.GradientStops[1].Color, sample / 20d);
                Assert.True(Contrast(onAccent, background) >= 4.5, $"Primary label at {key} sample {sample}");
                Assert.True(Contrast(onAccent, Blend(background, Colors.White, .075)) >= 4.5,
                    $"Primary hover label at {key} sample {sample}");
            }
        }

        // Check the decoded source and exact geometric mapping separately:
        // a smaller decode must never reintroduce the legacy offset/crop.
        var bitmap = Assert.IsType<BitmapImage>(app.FindResource("Flick.Image.BrandMark.Bitmap"));
        Assert.Equal(256, bitmap.DecodePixelWidth);
        Assert.InRange(bitmap.PixelWidth, 1, 256);
        Assert.InRange(bitmap.PixelHeight, 1, 256);
        var brand = Assert.IsType<DrawingImage>(app.FindResource("Flick.Image.BrandMark.Source"));
        var drawing = Assert.IsType<DrawingGroup>(brand.Drawing);
        var image = Assert.IsType<ImageDrawing>(drawing.Children[1]);
        Assert.Same(bitmap, image.ImageSource);
        Assert.Equal(new Rect(0, 0, 38, 38), image.Rect);
        var clip = Assert.IsType<EllipseGeometry>(drawing.ClipGeometry);
        Assert.Equal(new Point(19, 19), clip.Center);
        Assert.Equal(19, clip.RadiusX);
        Assert.Equal(19, clip.RadiusY);

        var primary = new Button { Content = "Copy file", Style = Style("Flick.Button.Primary") };
        var secondary = new Button { Content = "Open folder", Style = Style("Flick.Button.Secondary") };
        var text = new TextBox { Text = "Clip title", Style = Style("Flick.Input.Text") };
        var password = new PasswordBox { Password = "Fixture only", Style = Style("Flick.Input.Password") };
        var combo = new ComboBox { Style = Style("Flick.Input.Combo"), ItemsSource = new[] { "Original", "Export" }, SelectedIndex = 0 };
        var toggle = new CheckBox { Content = "Launch with Windows", Style = Style("Flick.Toggle") };
        var check = new CheckBox { Content = "Mute", Style = Style("Flick.Input.CheckBox") };
        var slider = new Slider { Style = Style("Flick.Input.Slider") };
        var filter = new RadioButton { Content = "All clips", Style = Style("Flick.Library.FilterChip") };
        var row = new ListBoxItem { Content = "Overtime finish", Style = Style("Flick.Library.Item") };
        var menu = new MenuItem { Header = "Open folder", Style = Assert.IsType<Style>(app.FindResource(typeof(MenuItem))) };
        (Control Control, string Part)[] controls =
        [
            (primary, "Chrome"), (secondary, "Chrome"), (text, "TextChrome"), (password, "PasswordChrome"),
            (combo, "DropButton"), (toggle, "ToggleTrack"), (check, "CheckFocus"),
            (slider, "SliderFocus"), (filter, "Chip"), (row, "ItemChrome"), (menu, "ActionRow")
        ];
        foreach (var (control, part) in controls)
        {
            Layout(control);
            var boundary = Assert.IsAssignableFrom<FrameworkElement>(control.Template.FindName(part, control));
            Thickness thickness = boundary switch
            {
                Border border => border.BorderThickness,
                Control child => child.BorderThickness,
                _ => throw new InvalidOperationException("Unknown focus boundary")
            };
            Assert.Equal(new Thickness(2), thickness);
            // These are minimum pointer targets in DIPs, not a maximum text
            // height. Font fallback and pixel rounding may grow natural size.
            Assert.InRange(control.MinHeight, 32, 36);
            double tolerance = .5 / VisualTreeHelper.GetDpi(control).DpiScaleY;
            Assert.True(control.ActualHeight + tolerance >= control.MinHeight,
                $"{part}: {control.ActualHeight} DIP is below its {control.MinHeight} DIP target");
            Assert.True(double.IsNaN(control.Height), $"{part} must retain a content-sized height");
            AssertLabelsFit(control);
            if (control is TextBox or PasswordBox)
                AssertEditorFits(control);
            // A future focus trigger must not resize the reserved boundary.
            foreach (var trigger in control.Template.Triggers.OfType<Trigger>().Where(t =>
                t.Property == UIElement.IsKeyboardFocusedProperty || t.Property == UIElement.IsKeyboardFocusWithinProperty))
                Assert.DoesNotContain(trigger.Setters.OfType<Setter>(), setter => setter.Property == Border.BorderThicknessProperty ||
                    setter.Property == Control.BorderThicknessProperty);
            Size desired = control.DesiredSize;
            control.IsEnabled = false;
            Layout(control);
            Assert.Equal(1, control.Opacity);
            Assert.Equal(Cursors.Arrow, control.Cursor);
            Assert.True(ToolTipService.GetShowOnDisabled(control));
            Assert.Equal(desired, control.DesiredSize);
            Assert.Same(Brush("Flick.Brush.TextMuted"), control.Foreground);
            control.IsEnabled = true;
        }

        // Selection is persistent after hover clears, with no content movement.
        Layout(row);
        var rowChrome = Assert.IsType<Border>(row.Template.FindName("ItemChrome", row));
        Size rowSize = row.DesiredSize;
        row.IsSelected = true;
        Layout(row);
        Assert.Same(Brush("Flick.Brush.BorderStrong"), rowChrome.BorderBrush);
        Assert.Same(Brush("Flick.Brush.Hover"), rowChrome.Background);
        Assert.Equal(rowSize, row.DesiredSize);
        filter.IsChecked = true;
        Layout(filter);
        var chip = Assert.IsType<Border>(filter.Template.FindName("Chip", filter));
        Assert.Same(Brush("Flick.Brush.BorderStrong"), chip.BorderBrush);

        // These are native patterns on styled controls, not synthetic accessible
        // names assigned by the fixture. Actual app icon names are checked separately.
        var buttonPeer = Assert.IsAssignableFrom<ButtonAutomationPeer>(UIElementAutomationPeer.CreatePeerForElement(primary));
        Assert.Equal("Copy file", buttonPeer.GetName());
        Assert.NotNull(buttonPeer.GetPattern(PatternInterface.Invoke));
        primary.IsEnabled = false;
        Assert.False(buttonPeer.IsEnabled());
        Assert.True(Contrast(Assert.IsType<SolidColorBrush>(primary.Foreground).Color,
            Assert.IsType<SolidColorBrush>(primary.Background).Color) >= 4.5);
        var togglePeer = Assert.IsAssignableFrom<CheckBoxAutomationPeer>(UIElementAutomationPeer.CreatePeerForElement(toggle));
        Assert.Equal("Launch with Windows", togglePeer.GetName());
        Assert.NotNull(togglePeer.GetPattern(PatternInterface.Toggle));
        Assert.NotNull(Assert.IsAssignableFrom<ComboBoxAutomationPeer>(UIElementAutomationPeer.CreatePeerForElement(combo))
            .GetPattern(PatternInterface.ExpandCollapse));
    }

    private static void Layout(Control control)
    {
        control.ApplyTemplate();
        control.Measure(new Size(300, double.PositiveInfinity));
        control.Arrange(new Rect(new Point(), control.DesiredSize));
        control.UpdateLayout();
    }

    private static void AssertEditorFits(Control control)
    {
        var host = Assert.IsType<ScrollViewer>(control.Template.FindName("PART_ContentHost", control));
        Assert.Equal(new Thickness(12, 4), control.Padding);
        Assert.Equal(control.Padding, host.Padding); // Native WPF padding forwarding.
        Assert.Equal(new Thickness(0), host.Margin);
        var hostParent = Assert.IsType<Grid>(VisualTreeHelper.GetParent(host));
        Assert.Equal(new Thickness(0), hostParent.Margin);
        Assert.Equal(14, control.FontSize);
        double tolerance = .5 / VisualTreeHelper.GetDpi(control).DpiScaleY;
        Assert.True(host.ExtentHeight > 0, "Populated editor must have measured text");
        Assert.True(host.ViewportHeight + tolerance >= host.ExtentHeight,
            $"{control.GetType().Name}: line {host.ExtentHeight} DIP exceeds viewport {host.ViewportHeight} DIP");
        // This pins the actual single-inset geometry, rather than merely raising
        // a height limit to accommodate a duplicated padding regression.
        double expected = Math.Max(control.MinHeight,
            host.DesiredSize.Height +
            control.BorderThickness.Top + control.BorderThickness.Bottom);
        Assert.InRange(control.ActualHeight, expected - tolerance, expected + tolerance);
    }

    private static void AssertLabelsFit(Control control)
    {
        foreach (var label in Descendants(control).OfType<TextBlock>().Where(t => !string.IsNullOrEmpty(t.Text)))
        {
            // Exercise Windows font metrics at the requested raster scales. This
            // is not a physical per-monitor DPI session or OS text-scale test.
            foreach (double scale in new[] { 1d, 1.25, 1.5, 2 })
            {
                var metrics = new FormattedText(label.Text, CultureInfo.CurrentUICulture, label.FlowDirection,
                    new Typeface(label.FontFamily, label.FontStyle, label.FontWeight, label.FontStretch),
                    label.FontSize, label.Foreground ?? Brushes.White, scale);
                double tolerance = .5 / scale;
                Assert.True(label.ActualHeight + tolerance >= metrics.Height,
                    $"{control.GetType().Name} label '{label.Text}' at {scale * 100}%: " +
                    $"line {metrics.Height} DIP exceeds {label.ActualHeight} DIP");
                Assert.True(label.ActualWidth + tolerance >= metrics.WidthIncludingTrailingWhitespace,
                    $"{control.GetType().Name} fixture label '{label.Text}' is clipped at {scale * 100}%");
            }
        }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var descendant in Descendants(child))
                yield return descendant;
        }
    }

    private static Color Blend(Color first, Color second, double amount) => Color.FromRgb(
        (byte)Math.Round(first.R + (second.R - first.R) * amount),
        (byte)Math.Round(first.G + (second.G - first.G) * amount),
        (byte)Math.Round(first.B + (second.B - first.B) * amount));

    private static double Contrast(Color first, Color second)
    {
        double a = Luminance(first), b = Luminance(second);
        return (Math.Max(a, b) + .05) / (Math.Min(a, b) + .05);
    }

    private static double Luminance(Color color)
    {
        static double Linear(byte channel)
        {
            double value = channel / 255d;
            return value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4);
        }
        return .2126 * Linear(color.R) + .7152 * Linear(color.G) + .0722 * Linear(color.B);
    }
}
