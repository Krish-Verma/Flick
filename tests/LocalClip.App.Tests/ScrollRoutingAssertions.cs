using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using LocalClip.App.Services;
using Xunit;

namespace LocalClip.App.Tests;

internal static class ScrollRoutingAssertions
{
    // Runs in the existing isolated STA/Application; no capture or user profile.
    public static void Run()
    {
        var app = Application.Current;
        var priorMain = app.MainWindow;
        var combo = new ComboBox { ItemsSource = new[] { "First", "Second", "Third" }, SelectedIndex = 1,
            Style = (Style)app.FindResource("Flick.Input.Combo") };
        Assert.True(FlickScrollRouting.GetPreserveSelectionOnWheel(combo));
        var content = new StackPanel();
        content.Children.Add(combo);
        content.Children.Add(new Border { Height = 1000 });
        var scroll = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var host = new Window { Content = scroll, Width = 300, Height = 180, ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual, Left = -3000, Top = -3000 };
        try
        {
            host.Show();
            host.UpdateLayout();
            host.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            Assert.True(combo.IsLoaded);
            Assert.True(scroll.ScrollableHeight > 0);
            var wheel = Wheel(combo);
            host.UpdateLayout();
            Assert.True(wheel.Handled);
            Assert.Equal(1, combo.SelectedIndex);
            Assert.True(scroll.VerticalOffset > 0, "The closed selector must scroll the page.");

            combo.IsDropDownOpen = true;
            host.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            Assert.True(combo.IsDropDownOpen, "Exercise an actual loaded native dropdown.");
            Assert.False(Wheel(combo).Handled);
            combo.IsDropDownOpen = false;
            // A capture-source selector with no scroll owner must still keep
            // its selection rather than changing recording targets on wheel.
            content.Children.Remove(combo);
            Assert.True(Wheel(combo).Handled);
            Assert.Equal(1, combo.SelectedIndex);
            FlickScrollRouting.SetPreserveSelectionOnWheel(combo, false);
            Assert.False(Wheel(combo).Handled);
        }
        finally
        {
            combo.IsDropDownOpen = false;
            host.Close();
            app.MainWindow = priorMain!;
        }
    }

    private static MouseWheelEventArgs Wheel(ComboBox combo)
    {
        var args = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120)
        { RoutedEvent = Mouse.PreviewMouseWheelEvent };
        combo.RaiseEvent(args);
        return args;
    }
}
