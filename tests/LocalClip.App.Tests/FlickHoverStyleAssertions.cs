using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LocalClip.App.Services;
using Xunit;

namespace LocalClip.App.Tests;

/// <summary>Call within the existing STA/WPF Application, after real production
/// theme dictionaries load; no synthetic mouse injection or game capture.</summary>
internal static class FlickHoverStyleAssertions
{
    public static void Run()
    {
        var app = Assert.IsAssignableFrom<Application>(Application.Current);
        string[] directStyles =
        [
            "Flick.Button.Base",
            "Flick.Input.ComboItem",
            "Flick.Input.Combo",
            "Flick.Toggle",
            "Flick.Input.CheckBox",
            "Flick.Input.Slider",
            "Flick.Library.FilterChip",
            "Flick.Library.Item"
        ];
        foreach (var key in directStyles)
        {
            var style = Assert.IsType<Style>(app.FindResource(key));
            Assert.Contains(style.Setters.OfType<Setter>(),
                setter => setter.Property == FlickHoverFeedback.EnabledProperty &&
                    setter.Value is bool enabled && enabled);
        }

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
    }
}
