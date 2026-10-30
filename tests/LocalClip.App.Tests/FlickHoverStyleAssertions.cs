using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LocalClip.App.Services;
using Xunit;

namespace LocalClip.App.Tests;

/// <summary>Call within the existing STA/WPF Application, after real production
/// theme dictionaries load; no synthetic mouse injection or game capture.</summary>
internal static class FlickHoverStyleAssertions
{
    public static void Run()
    {
        FlickThemeAccessibilityAssertions.Run();
        var app = Assert.IsAssignableFrom<Application>(Application.Current);
        string[] directStyles =
        [
            "Flick.Button.Base",
            "Flick.Input.Text",
            "Flick.Input.Password",
            "Flick.Input.ComboItem",
            "Flick.Input.Combo",
            "Flick.Toggle",
            "Flick.Input.CheckBox",
            "Flick.Input.Slider",
            "Flick.Library.FilterChip",
            "Flick.Library.Item",
            "Flick.Scrollbar.Thumb"
        ];
        foreach (var key in directStyles)
        {
            var style = Assert.IsType<Style>(app.FindResource(key));
            Assert.Contains(style.Setters.OfType<Setter>(),
                setter => setter.Property == FlickHoverFeedback.EnabledProperty &&
                    setter.Value is bool enabled && enabled);
        }

        var menuStyle = Assert.IsType<Style>(app.FindResource(typeof(System.Windows.Controls.MenuItem)));
        Assert.Contains(menuStyle.Setters.OfType<Setter>(),
            setter => setter.Property == FlickHoverFeedback.EnabledProperty &&
                setter.Value is bool enabled && enabled);

        var primary = new Button { Style = Assert.IsType<Style>(app.FindResource("Flick.Button.Primary")) };
        primary.ApplyTemplate();
        Assert.True(FlickHoverFeedback.GetEnabled(primary));
        Assert.Equal(Cursors.Hand, primary.Cursor);

        var combo = new ComboBox { Style = Assert.IsType<Style>(app.FindResource("Flick.Input.Combo")) };
        combo.ApplyTemplate();
        Assert.True(FlickHoverFeedback.GetEnabled(combo));
        Assert.Equal(Cursors.Hand, combo.Cursor);

        var toggle = new CheckBox { Style = Assert.IsType<Style>(app.FindResource("Flick.Toggle")) };
        toggle.ApplyTemplate();
        Assert.True(FlickHoverFeedback.GetEnabled(toggle));
        Assert.Equal(Cursors.Hand, toggle.Cursor);

        // Disabled controls remain disabled and don't acquire a spurious
        // command merely because hover visual resources exist.
        primary.IsEnabled = false;
        Assert.False(primary.IsEnabled);
        Assert.True(FlickHoverFeedback.GetEnabled(primary));

        // Exercise the actual rendering operation and production templates.
        // No mouse injection, global accessibility mutation or recorder starts.
        var feedback = typeof(FlickHoverFeedback).GetMethod("ApplyFeedback", BindingFlags.Static | BindingFlags.NonPublic)!;
        Control[] controls =
        [
            new Button { Content = "Save", Style = (Style)app.FindResource("Flick.Button.Primary") },
            new Button { Content = "Cancel", Style = (Style)app.FindResource("Flick.Button.Secondary") },
            new Button { Content = "Details", Style = (Style)app.FindResource("Flick.Button.Ghost") },
            new TextBox { Text = "Clip name", Style = (Style)app.FindResource("Flick.Input.Text") },
            new PasswordBox { Style = (Style)app.FindResource("Flick.Input.Password") },
            new ComboBox { Style = (Style)app.FindResource("Flick.Input.Combo") },
            new ComboBoxItem { Content = "Original", Style = (Style)app.FindResource("Flick.Input.ComboItem") },
            new CheckBox { Content = "Launch with Windows", Style = (Style)app.FindResource("Flick.Toggle") },
            new CheckBox { Content = "Mute", Style = (Style)app.FindResource("Flick.Input.CheckBox") },
            new RadioButton { Content = "All clips", Style = (Style)app.FindResource("Flick.Library.FilterChip") },
            new ListBoxItem { Content = "Overtime finish", Style = (Style)app.FindResource("Flick.Library.Item") },
            new Slider { Style = (Style)app.FindResource("Flick.Input.Slider") },
            new Thumb { Style = (Style)app.FindResource("Flick.Scrollbar.Thumb") },
            new MenuItem { Header = "Open folder", Style = (Style)app.FindResource(typeof(MenuItem)) }
        ];
        foreach (var control in controls)
        {
            control.ApplyTemplate();
            control.Measure(new Size(250, 50));
            control.Arrange(new Rect(0, 0, 250, 50));
            control.UpdateLayout();
            var surface = Assert.IsType<Border>(control.Template.FindName(FlickHoverFeedback.SurfacePartName, control));
            Assert.False(surface.IsHitTestVisible);
            var background = control.Background;
            double opacity = control.Opacity;
            Size desired = control.DesiredSize;
            byte[]? restingPixel = control is Button && ReferenceEquals(control.Style, app.FindResource("Flick.Button.Primary"))
                ? Pixel(control) : null;

            feedback.Invoke(null, [control, true, false, false]);
            Assert.InRange(surface.Opacity, .06, .10);
            Assert.Equal(Colors.White, Assert.IsType<SolidColorBrush>(surface.Background).Color);
            Assert.Same(background, control.Background); // Primary gradients remain gradients.
            Assert.Equal(opacity, control.Opacity); // Text/icons are never dimmed.
            Assert.False(surface.HasAnimatedProperties); // Reduced motion still has feedback.
            if (restingPixel != null)
            {
                byte[] brightenedPixel = Pixel(control);
                Assert.True(brightenedPixel[0] >= restingPixel[0] && brightenedPixel[1] >= restingPixel[1] &&
                    brightenedPixel[2] >= restingPixel[2]);
                Assert.True(brightenedPixel[0] > restingPixel[0] || brightenedPixel[1] > restingPixel[1] ||
                    brightenedPixel[2] > restingPixel[2]);
                Assert.Equal(restingPixel[3], brightenedPixel[3]);
            }
            control.Measure(new Size(250, 50));
            Assert.Equal(desired, control.DesiredSize);

            feedback.Invoke(null, [control, false, false, false]);
            Assert.Equal(0, surface.Opacity);
            feedback.Invoke(null, [control, true, true, false]);
            Assert.True(surface.HasAnimatedProperties);
            control.IsEnabled = false;
            feedback.Invoke(null, [control, true, true, false]);
            Assert.Equal(0, surface.Opacity);
            Assert.False(surface.HasAnimatedProperties);

            control.IsEnabled = true;
            feedback.Invoke(null, [control, true, true, true]);
            Assert.Equal(1, surface.Opacity);
            Assert.Null(surface.Background); // High contrast uses a system outline.
            Assert.Same(SystemColors.HighlightBrush, surface.BorderBrush);
            Assert.Equal(new Thickness(2), surface.BorderThickness);
            Assert.False(surface.HasAnimatedProperties);
            feedback.Invoke(null, [control, false, false, false]);
            Assert.Equal(0, surface.Opacity);
        }

        static byte[] Pixel(Control control)
        {
            var bitmap = new RenderTargetBitmap(250, 50, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(control);
            var pixel = new byte[4];
            bitmap.CopyPixels(new Int32Rect(12, 12, 1, 1), pixel, 4, 0);
            return pixel;
        }
    }
}
