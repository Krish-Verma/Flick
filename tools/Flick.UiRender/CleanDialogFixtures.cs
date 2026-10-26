using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using LocalClip.App;
using LocalClip.App.Services;
using LocalClip.App.ViewModels;
using LocalClip.Core.Models;

namespace Flick.UiRender;

internal static partial class Program
{
    private static void RenderCleanDialogFixtures(MainWindow window, MainViewModel vm, string output,
        Action<string, int, int> render)
    {
        void Invoke(string method, params object?[] values) => typeof(MainWindow).GetMethod(method, Private)!.Invoke(window, values);
        var root = (FrameworkElement)window.Content;
        byte[] preferencesBefore = File.ReadAllBytes(AppSettings.GetSettingsFilePath());
        string? originalTitle = vm.SelectedClip?.Title;
        string originalExportStatus = vm.ExportStatusMessage;
        var observations = new List<object>();
        try
        {
            Invoke("ReturnToClipWorkspace");
            Invoke("CloseEditorContext");
            DialogFrame("131-rename-dialog-wide.png", 1280, 720, "Cancel", expectInput: true,
                () => Invoke("BtnRename_Click", window, new RoutedEventArgs()));
            DialogFrame("132-rename-dialog-minimum.png", 720, 480, "Cancel", expectInput: true,
                () => Invoke("BtnRename_Click", window, new RoutedEventArgs()));
            vm.ExportStatusMessage = "Synthetic retained export detail · no export is running.";
            DialogFrame("133-share-details-dialog-minimum.png", 720, 480, "OK", expectInput: false,
                () => Invoke("ShareDetails_Click", window, new RoutedEventArgs()));
            DialogFrame("134-long-detail-dialog-minimum.png", 720, 480, "OK", expectInput: false,
                () => FlickDialog.Show("Synthetic long retained detail: " +
                    string.Join(" ", Enumerable.Repeat("A local file was unavailable. Choose an accessible folder and try again.", 20)),
                    "Synthetic Details"), requireOverflow: true);
            if (vm.SelectedClip?.Title != originalTitle || !preferencesBefore.SequenceEqual(File.ReadAllBytes(AppSettings.GetSettingsFilePath())) ||
                vm.CaptureHost.IsRunning || vm.DiagnosticsInitialized || FlickDialog.IsOpen)
                throw new InvalidOperationException("Dialog fixtures changed clip/preferences or left a live subsystem/modal running.");
        }
        finally { vm.ExportStatusMessage = originalExportStatus; }

        void DialogFrame(string file, int width, int height, string safeLabel, bool expectInput, Action open, bool requireOverflow = false)
        {
            bool observed = false;
            Exception? failure = null;
            window.Dispatcher.BeginInvoke(new Action(() =>
            {
                var host = (ContentControl)window.FindName("DialogContentHost");
                Button? safe = null;
                try
                {
                    // Exercise the actual modal and caption/input/action styles.
                    // Rendering the unshown client never opens a native picker.
                    render(file, width, height);
                    safe = ProductVisuals(host).OfType<Button>().Single(button => Equals(button.Content, safeLabel));
                    var buttons = ProductVisuals(host).OfType<Button>().ToArray();
                    var inputs = ProductVisuals(host).OfType<TextBox>().ToArray();
                    // A single-line TextBox owns an internal content host; it
                    // is not another independently scrolling dialog region.
                    var scrollers = ProductVisuals(host).OfType<ScrollViewer>()
                        .Where(candidate => inputs.All(input => !HasVisualAncestor(candidate, input, host))).ToArray();
                    Rect bounds = new(root.RenderSize);
                    bool actionsFit = buttons.All(button => ProductContains(bounds, RelativeBounds(button, root)));
                    bool singleBodyScroll = scrollers.Length == 1 && scrollers[0].ViewportHeight > 0 &&
                        double.IsFinite(scrollers[0].ViewportHeight) &&
                        buttons.All(button => !HasVisualAncestor(button, scrollers[0], host));
                    bool inputFits = inputs.Length == (expectInput ? 1 : 0) &&
                        inputs.All(input => ProductContains(bounds, RelativeBounds(input, root)));
                    Rect[] actionBounds = buttons.Select(button => RelativeBounds(button, root)).ToArray();
                    bool overflowWorks = !requireOverflow;
                    bool fixedActions = true;
                    if (requireOverflow && scrollers.Length == 1)
                    {
                        scrollers[0].ScrollToBottom();
                        root.UpdateLayout();
                        root.Dispatcher.Invoke(() => { }, DispatcherPriority.Render);
                        root.UpdateLayout();
                        overflowWorks = scrollers[0].ScrollableHeight > 0 && scrollers[0].VerticalOffset > 0;
                        fixedActions = actionBounds.SequenceEqual(buttons.Select(button => RelativeBounds(button, root)));
                        render(file, width, height);
                    }
                    observed = FlickDialog.IsOpen;
                    observations.Add(new { Image = file, ActionsFit = actionsFit, SingleBodyScroll = singleBodyScroll,
                        InputFits = inputFits, InputExpected = expectInput, OverflowRequired = requireOverflow,
                        OverflowWorks = overflowWorks, FixedActions = fixedActions, SafeAction = safeLabel,
                        NoNativeWindowOrConfirmedMutation = true });
                    File.WriteAllText(Path.Combine(output, "clean-dialog-fixture-evidence.json"), JsonSerializer.Serialize(new {
                        Scope = "Actual production same-window WPF Rename/Export Details; rename is cancelled. One additional synthetic long message verifies the shared modal body scroller and fixed safe action. No file picker, rename, save, upload, capture or external operation.",
                        Observations = observations
                    }, new JsonSerializerOptions { WriteIndented = true }));
                    if (!observed || !actionsFit || !singleBodyScroll || !inputFits || !overflowWorks || !fixedActions)
                        throw new InvalidOperationException("Modal geometry or scrolling regressed in " + file);
                }
                catch (Exception error) { failure = error; }
                finally
                {
                    safe ??= ProductVisuals(host).OfType<Button>().FirstOrDefault(button => Equals(button.Content, safeLabel));
                    safe?.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
            }), DispatcherPriority.Normal);
            open();
            if (failure != null) throw new InvalidOperationException("Dialog rendering failed.", failure);
            if (!observed || FlickDialog.IsOpen) throw new InvalidOperationException("The dialog did not open and safely close in " + file);
        }
    }
}
