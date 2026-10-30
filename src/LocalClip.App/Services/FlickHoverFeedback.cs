using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using Brushes = System.Windows.Media.Brushes;
using SystemColors = System.Windows.SystemColors;

namespace LocalClip.App.Services;

/// <summary>
/// Brightens a template-owned surface without changing its base brush, text,
/// opacity, layout or input behavior. Popups use the same lightweight part;
/// no adorner layer, render-loop, polling or effects are required.
/// </summary>
public static class FlickHoverFeedback
{
    public const string SurfacePartName = "PART_FlickHoverSurface";
    private const double HighlightOpacity = .075;
    private static readonly Duration Transition = new(TimeSpan.FromMilliseconds(125));
    private static readonly List<WeakReference<FrameworkElement>> Active = [];
    private static readonly object ActiveGate = new();

    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(FlickHoverFeedback), new PropertyMetadata(false, OnEnabledChanged));
    public static readonly DependencyProperty SurfaceProperty = DependencyProperty.RegisterAttached(
        "Surface", typeof(Border), typeof(FlickHoverFeedback), new PropertyMetadata(null));
    private static readonly DependencyProperty ListeningProperty = DependencyProperty.RegisterAttached(
        "Listening", typeof(bool), typeof(FlickHoverFeedback), new PropertyMetadata(false));

    static FlickHoverFeedback() => SystemParameters.StaticPropertyChanged += PreferencesChanged;

    public static void SetEnabled(DependencyObject target, bool value) => target.SetValue(EnabledProperty, value);
    public static bool GetEnabled(DependencyObject target) => (bool)target.GetValue(EnabledProperty);
    public static void SetSurface(DependencyObject target, Border? value) => target.SetValue(SurfaceProperty, value);
    public static Border? GetSurface(DependencyObject target) => (Border?)target.GetValue(SurfaceProperty);

    private static void OnEnabledChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is not FrameworkElement element) return;
        element.MouseEnter -= PointerChanged;
        element.MouseLeave -= PointerChanged;
        element.Loaded -= Loaded;
        element.Unloaded -= Unloaded;
        element.IsEnabledChanged -= EnabledChanged;
        SetHighlightListening(element, false);
        if ((bool)args.NewValue)
        {
            element.MouseEnter += PointerChanged;
            element.MouseLeave += PointerChanged;
            element.Loaded += Loaded;
            element.Unloaded += Unloaded;
            element.IsEnabledChanged += EnabledChanged;
            if (element.IsLoaded) { SetHighlightListening(element, true); Refresh(element); }
        }
        else ApplyFeedback(element, false, false, SystemParameters.HighContrast);
    }

    private static DependencyPropertyDescriptor? HighlightDescriptor(FrameworkElement element) => element switch
    {
        ComboBoxItem => DependencyPropertyDescriptor.FromProperty(ComboBoxItem.IsHighlightedProperty, typeof(ComboBoxItem)),
        MenuItem => DependencyPropertyDescriptor.FromProperty(MenuItem.IsHighlightedProperty, typeof(MenuItem)),
        _ => null
    };

    private static void SetHighlightListening(FrameworkElement element, bool value)
    {
        if ((bool)element.GetValue(ListeningProperty) == value) return;
        var descriptor = HighlightDescriptor(element);
        if (value) descriptor?.AddValueChanged(element, HighlightChanged);
        else descriptor?.RemoveValueChanged(element, HighlightChanged);
        element.SetValue(ListeningProperty, value);
    }

    private static void Loaded(object sender, RoutedEventArgs args)
    {
        if (sender is not FrameworkElement element) return;
        SetHighlightListening(element, true);
        Refresh(element);
    }

    private static void Unloaded(object sender, RoutedEventArgs args)
    {
        if (sender is not FrameworkElement element) return;
        SetHighlightListening(element, false);
        ApplyFeedback(element, false, false, SystemParameters.HighContrast);
    }

    private static void PointerChanged(object sender, MouseEventArgs args)
    { if (sender is FrameworkElement element) Refresh(element); }

    private static void HighlightChanged(object? sender, EventArgs args)
    { if (sender is FrameworkElement element) Refresh(element); }

    private static void EnabledChanged(object sender, DependencyPropertyChangedEventArgs args)
    { if (sender is FrameworkElement element) Refresh(element); }

    private static void Refresh(FrameworkElement element)
    {
        bool highlighted = element.IsMouseOver || element is ComboBoxItem { IsHighlighted: true }
            || element is MenuItem { IsHighlighted: true };
        ApplyFeedback(element, highlighted, FlickVisualPreferences.ShouldAnimate, SystemParameters.HighContrast);
    }

    private static Border? FindSurface(FrameworkElement element)
    {
        if (GetSurface(element) is { } explicitSurface) return explicitSurface;
        if (element is Control control && control.Template?.FindName(SurfacePartName, control) is Border surface)
            return surface;
        return null;
    }

    // One deterministic rendering operation is shared by pointer/keyboard
    // highlight events, OS-preference changes and the real WPF visual tests.
    private static void ApplyFeedback(FrameworkElement element, bool highlighted, bool animate, bool highContrast)
    {
        var surface = FindSurface(element);
        if (surface == null) return;
        highlighted &= element.IsEnabled && GetEnabled(element);
        double current = surface.Opacity;
        surface.BeginAnimation(UIElement.OpacityProperty, null);
        surface.Background = highContrast ? null : Brushes.White;
        surface.BorderBrush = highContrast ? SystemColors.HighlightBrush : null;
        surface.BorderThickness = highContrast ? new Thickness(2) : new Thickness(0);
        double target = highlighted ? (highContrast ? 1 : HighlightOpacity) : 0;
        surface.SetCurrentValue(UIElement.OpacityProperty, target);
        if (animate && !highContrast && element.IsEnabled && Math.Abs(current - target) > .001)
            surface.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(current, target, Transition)
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.Stop
            });
        lock (ActiveGate)
        {
            Active.RemoveAll(reference => !reference.TryGetTarget(out var item) || ReferenceEquals(item, element));
            if (highlighted) Active.Add(new WeakReference<FrameworkElement>(element));
        }
    }

    private static void PreferencesChanged(object? sender, PropertyChangedEventArgs args)
    {
        // Only currently highlighted controls are tracked, and only weakly.
        // Preference changes never allocate a per-control timer or retain a page.
        WeakReference<FrameworkElement>[] targets;
        lock (ActiveGate) targets = Active.ToArray();
        foreach (var reference in targets)
        {
            if (!reference.TryGetTarget(out var element)) continue;
            if (element.Dispatcher.HasShutdownStarted || element.Dispatcher.HasShutdownFinished) continue;
            if (element.Dispatcher.CheckAccess()) Refresh(element);
            else _ = element.Dispatcher.BeginInvoke(new Action(() => Refresh(element)));
        }
    }
}
