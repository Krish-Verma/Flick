using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace LocalClip.App.Services;

/// <summary>
/// Opt-in, event-driven, very small hover/focus affordance for genuinely
/// interactive WPF controls. No render-loop, polling, transforms, shaders,
/// game-window overlays or background threads are introduced.
/// </summary>
public static class FlickHoverFeedback
{
    public static readonly DependencyProperty EnabledProperty =
        DependencyProperty.RegisterAttached("Enabled", typeof(bool), typeof(FlickHoverFeedback),
            new PropertyMetadata(false, OnEnabledChanged));

    public static void SetEnabled(DependencyObject target, bool enabled) =>
        target.SetValue(EnabledProperty, enabled);

    public static bool GetEnabled(DependencyObject target) =>
        (bool)target.GetValue(EnabledProperty);

    // Tiny pressed/hover lift in combination with the existing theme brush
    // change. Never dim the control under High Contrast or Reduced Motion.
    private const double HoverOpacity = .94;
    private static readonly Duration HoverDuration = new(TimeSpan.FromMilliseconds(125));

    private static void OnEnabledChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is not FrameworkElement element) return;
        element.MouseEnter -= MouseEnter;
        element.MouseLeave -= MouseLeave;
        element.IsEnabledChanged -= ElementEnabledChanged;
        if ((bool)args.NewValue)
        {
            element.MouseEnter += MouseEnter;
            element.MouseLeave += MouseLeave;
            element.IsEnabledChanged += ElementEnabledChanged;
        }
        else
        {
            element.BeginAnimation(UIElement.OpacityProperty, null);
        }
    }

    private static void MouseEnter(object sender, MouseEventArgs args)
    {
        if (sender is FrameworkElement element) Animate(element, hovered: true);
    }

    private static void MouseLeave(object sender, MouseEventArgs args)
    {
        if (sender is FrameworkElement element) Animate(element, hovered: false);
    }

    private static void ElementEnabledChanged(object sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is FrameworkElement element)
        {
            // Releasing this animation restores the template's own disabled
            // opacity and keyboard/high-contrast states immediately.
            element.BeginAnimation(UIElement.OpacityProperty, null);
            if (element.IsEnabled && element.IsMouseOver) Animate(element, hovered: true);
        }
    }

    private static void Animate(FrameworkElement element, bool hovered)
    {
        if (!element.IsEnabled || !FlickVisualPreferences.ShouldAnimate)
        {
            element.BeginAnimation(UIElement.OpacityProperty, null);
            return;
        }
        element.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(hovered ? HoverOpacity : 1.0, HoverDuration)
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.HoldEnd
            });
    }
}
