using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace LocalClip.App.Services;

/// <summary>Scrolling a form must not silently edit a closed selection control.</summary>
public static class FlickScrollRouting
{
    public static readonly DependencyProperty PreserveSelectionOnWheelProperty = DependencyProperty.RegisterAttached(
        "PreserveSelectionOnWheel", typeof(bool), typeof(FlickScrollRouting),
        new PropertyMetadata(false, Changed));

    public static bool GetPreserveSelectionOnWheel(DependencyObject target) =>
        (bool)target.GetValue(PreserveSelectionOnWheelProperty);

    public static void SetPreserveSelectionOnWheel(DependencyObject target, bool value) =>
        target.SetValue(PreserveSelectionOnWheelProperty, value);

    private static void Changed(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is not ComboBox combo) return;
        if ((bool)args.OldValue) combo.PreviewMouseWheel -= OnWheel;
        if ((bool)args.NewValue) combo.PreviewMouseWheel += OnWheel;
    }

    private static void OnWheel(object sender, MouseWheelEventArgs args)
    {
        if (sender is not ComboBox combo || combo.IsDropDownOpen) return;
        // Leave the open dropdown's native scrolling and keyboard selection intact.
        // A closed combo forwards the original wheel delta to the form's scroll
        // owner, preserving Windows' wheel preferences and precise input behavior.
        args.Handled = true;
        DependencyObject? parent = VisualTreeHelper.GetParent(combo);
        while (parent != null)
        {
            if (parent is ScrollViewer scroll)
            {
                scroll.RaiseEvent(new MouseWheelEventArgs(args.MouseDevice, args.Timestamp, args.Delta)
                {
                    RoutedEvent = Mouse.MouseWheelEvent,
                    Source = scroll
                });
                return;
            }
            parent = VisualTreeHelper.GetParent(parent);
        }
    }
}
