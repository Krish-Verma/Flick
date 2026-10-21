using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Microsoft.Win32;

namespace LocalClip.App.Services;

/// <summary>
/// Follows operating-system accessibility and transparency preferences.
/// No frame polling, shader, separate process or background refresh timer.
/// Existing WPF DynamicResource controls update in place.
/// </summary>
public sealed class FlickVisualPreferences : IDisposable
{
    private readonly Application _app;
    private bool _disposed;

    private static readonly IReadOnlyDictionary<string, Brush> ContrastBrushes =
        new Dictionary<string, Brush>
        {
            ["Flick.Brush.Canvas"] = SystemColors.WindowBrush,
            ["Flick.Brush.Surface"] = SystemColors.WindowBrush,
            ["Flick.Brush.Raised"] = SystemColors.ControlBrush,
            ["Flick.Brush.Hover"] = SystemColors.ControlBrush,
            ["Flick.Brush.Frosted"] = SystemColors.WindowBrush,
            ["Flick.Brush.Toast"] = SystemColors.WindowBrush,
            ["Flick.Brush.TextPrimary"] = SystemColors.WindowTextBrush,
            ["Flick.Brush.TextSecondary"] = SystemColors.WindowTextBrush,
            ["Flick.Brush.TextMuted"] = SystemColors.WindowTextBrush,
            ["Flick.Brush.Accent"] = SystemColors.HighlightBrush,
            ["Flick.Brush.AccentHover"] = SystemColors.HighlightBrush,
            ["Flick.Brush.OnAccent"] = SystemColors.HighlightTextBrush,
            ["Flick.Brush.Border"] = SystemColors.WindowTextBrush,
            ["Flick.Brush.BorderStrong"] = SystemColors.WindowTextBrush,
            ["Flick.Brush.Focus"] = SystemColors.HighlightBrush,
            ["Flick.Brush.Success"] = SystemColors.WindowTextBrush,
            ["Flick.Brush.Warning"] = SystemColors.WindowTextBrush,
            ["Flick.Brush.Danger"] = SystemColors.WindowTextBrush
        };

    public static bool ShouldAnimate =>
        !SystemParameters.HighContrast && SystemParameters.ClientAreaAnimation;

    public static bool CanUseTransparency
    {
        get
        {
            if (SystemParameters.HighContrast) return false;
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                return key?.GetValue("EnableTransparency") is not int enabled || enabled != 0;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.IO.IOException)
            {
                return false; // Respect the more conservative opaque fallback.
            }
        }
    }

    public FlickVisualPreferences(Application app)
    {
        _app = app ?? throw new ArgumentNullException(nameof(app));
        Apply();
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    private void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs args)
    {
        if (_disposed) return;
        if (_app.Dispatcher.CheckAccess()) Apply();
        else _ = _app.Dispatcher.BeginInvoke(new Action(Apply));
    }

    private void Apply()
    {
        if (_disposed) return;
        if (SystemParameters.HighContrast)
        {
            foreach (var (key, brush) in ContrastBrushes)
                _app.Resources[key] = brush;
        }
        else
        {
            // Removing local overrides reveals the original design-token
            // brushes in App.xaml's merged resource dictionaries.
            foreach (var key in ContrastBrushes.Keys)
                _app.Resources.Remove(key);
        }

        foreach (Window window in _app.Windows)
        {
            if (window is not MainWindow || !window.IsLoaded) continue;
            if (CanUseTransparency)
                _ = FlickBackdropService.TryApplyMica(window);
            else FlickBackdropService.Reset(window);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
    }
}
