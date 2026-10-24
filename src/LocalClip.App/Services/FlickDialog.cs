using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace LocalClip.App.Services;

/// <summary>
/// Themed, main-window-only consent. The nested dispatcher preserves the existing
/// synchronous source-change contract while capture/IPC continues. No new HWND.
/// Reentrant requests fail closed rather than stacking prompts.
/// </summary>
public static class FlickDialog
{
    public static bool IsOpen { get; private set; }

    public static MessageBoxResult Show(string message, string caption,
        MessageBoxButton buttons = MessageBoxButton.OK,
        MessageBoxImage image = MessageBoxImage.None,
        MessageBoxResult defaultResult = MessageBoxResult.None) =>
        Display(message, caption, buttons, null).Result;

    public static MessageBoxResult Show(Window owner, string message, string caption,
        MessageBoxButton buttons = MessageBoxButton.OK,
        MessageBoxImage image = MessageBoxImage.None,
        MessageBoxResult defaultResult = MessageBoxResult.None) =>
        Show(message, caption, buttons, image, defaultResult);

    public static string? Prompt(string caption, string message, string initial)
    {
        var result = Display(message, caption, MessageBoxButton.OKCancel, initial);
        return result.Result == MessageBoxResult.OK ? result.Text : null;
    }

    private static (MessageBoxResult Result, string? Text) Display(
        string message, string caption, MessageBoxButton buttons, string? initial)
    {
        if (System.Windows.Application.Current?.MainWindow is not MainWindow main ||
            main.Dispatcher.HasShutdownStarted || !main.Dispatcher.CheckAccess() || IsOpen)
            return (MessageBoxResult.Cancel, null);

        var priorFocus = Keyboard.FocusedElement;
        bool wasEnabled = main.InteractionSurface.IsEnabled;
        bool confirmation = buttons != MessageBoxButton.OK;
        var frame = new DispatcherFrame();
        MessageBoxResult result = MessageBoxResult.Cancel;
        var layout = new DockPanel { Margin = new Thickness(22), LastChildFill = true };
        var heading = new TextBlock { Text = caption,
            Style = (Style)main.FindResource("Flick.Text.Section"),
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(heading, Dock.Top);
        layout.Children.Add(heading);
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = message,
            Style = (Style)main.FindResource("Flick.Text.Control"),
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 18) });
        TextBox? input = null;
        if (initial != null)
        {
            input = new TextBox { Text = initial,
                Style = (Style)main.FindResource("Flick.Input.Text"),
                Margin = new Thickness(0, 0, 0, 16) };
            System.Windows.Automation.AutomationProperties.SetName(input, caption);
            panel.Children.Add(input);
        }
        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        void Finish(MessageBoxResult choice) { result = choice; frame.Continue = false; }
        Button Add(string label, MessageBoxResult choice, bool primary)
        {
            var button = new Button { Content = label, MinWidth = 88,
                Margin = new Thickness(8, 4, 0, 0),
                Style = (Style)main.FindResource(primary ? "Flick.Button.Primary" : "Flick.Button.Secondary") };
            button.Click += (_, _) => Finish(choice);
            actions.Children.Add(button);
            return button;
        }
        Button safe;
        if (confirmation)
        {
            safe = Add(buttons == MessageBoxButton.YesNo ? "No" : "Cancel",
                buttons == MessageBoxButton.YesNo ? MessageBoxResult.No : MessageBoxResult.Cancel, false);
            Add(buttons is MessageBoxButton.YesNo or MessageBoxButton.YesNoCancel ? "Yes" : "Save",
                buttons is MessageBoxButton.YesNo or MessageBoxButton.YesNoCancel ? MessageBoxResult.Yes : MessageBoxResult.OK, true);
        }
        else safe = Add("OK", MessageBoxResult.OK, true);
        // Exact window/device names can be long. Keep safe consent actions
        // visible while the message scrolls inside the available window.
        DockPanel.SetDock(actions, Dock.Bottom);
        layout.Children.Add(actions);
        var scroll = new ScrollViewer { Content = panel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Focusable = false };
        layout.Children.Add(scroll);
        var surface = new Border { Child = layout,
            Style = (Style)main.FindResource("Flick.Surface.Panel") };
        KeyboardNavigation.SetTabNavigation(surface, KeyboardNavigationMode.Cycle);
        KeyEventHandler keys = (_, e) =>
        {
            if (e.Key == Key.Escape) { Finish(MessageBoxResult.Cancel); e.Handled = true; }
        };
        EventHandler closed = (_, _) => Finish(MessageBoxResult.Cancel);
        try
        {
            IsOpen = true;
            main.InteractionSurface.IsEnabled = false;
            main.DialogHost.Content = surface;
            main.ModalLayer.Visibility = Visibility.Visible;
            main.ModalLayer.PreviewKeyDown += keys;
            main.Closed += closed;
            safe.Focus(); // Destructive actions never receive default Enter focus.
            if (input != null) { input.Focus(); input.SelectAll(); }
            Dispatcher.PushFrame(frame);
            return (result, input?.Text);
        }
        finally
        {
            main.Closed -= closed;
            main.ModalLayer.PreviewKeyDown -= keys;
            main.ModalLayer.Visibility = Visibility.Collapsed;
            main.DialogHost.Content = null;
            main.InteractionSurface.IsEnabled = wasEnabled;
            IsOpen = false;
            if (priorFocus != null) Keyboard.Focus(priorFocus);
        }
    }
}
