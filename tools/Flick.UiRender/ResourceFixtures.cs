using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LocalClip.App;
using LocalClip.App.ViewModels;
using LocalClip.App.Views;
using LocalClip.Core.Diagnostics;
using LocalClip.Core.Ipc;
using LocalClip.Core.Rules;

namespace Flick.UiRender;

internal static partial class Program
{
    private static void RenderResourceFixtures(MainWindow window, MainViewModel vm, string output, Action<string, int, int> render)
    {
        const long mib = 1024 * 1024;
        void Invoke(string method, params object?[] values) => typeof(MainWindow).GetMethod(method, Private)!.Invoke(window, values);
        var publish = typeof(MainViewModel).GetMethod("PublishResourceSample", Private)
            ?? throw new InvalidOperationException("The production resource projection seam is unavailable.");
        string originalText = vm.ResourceStatsText, originalDetails = vm.ResourceStatsDetails, originalStatus = vm.StatusDetails;
        bool originalPreference = vm.Settings.ShowResourceUsage;
        var samplerField = typeof(MainViewModel).GetField("_resourceSampleAsync", Private)!;
        object? originalSampler = samplerField.GetValue(vm);
        object? originalRefresh = typeof(MainViewModel).GetField("_resourceRefreshTask", Private)!.GetValue(vm);
        int unexpectedQueries = 0;
        samplerField.SetValue(vm, new Func<int?, CancellationToken, Task<ResourceSample>>((_, _) =>
        {
            Interlocked.Increment(ref unexpectedQueries);
            throw new InvalidOperationException("A resource rendering fixture must never query processes.");
        }));
        byte[] originalPreferences = File.ReadAllBytes(LocalClip.Core.Models.AppSettings.GetSettingsFilePath());
        SettingsWindow? resourcePage = null;
        object? originalProfile = typeof(MainViewModel).GetField("_captureProfile", Private)!.GetValue(vm);
        var setProfile = typeof(MainViewModel).GetMethod("SetCaptureProfile", Private)!;
        var originalMode = typeof(MainWindow).GetField("_layoutMode", Private)!.GetValue(window);
        var observations = new List<object>();
        bool restored = false;
        bool timerStopped = !((System.Windows.Threading.DispatcherTimer)typeof(MainViewModel).GetField("_pollTimer", Private)!.GetValue(vm)!).IsEnabled;
        if (!timerStopped) throw new InvalidOperationException("Synthetic resource views require the real polling timer to remain stopped.");
        void Frame(string file, string kind, ResourceSample? sample, int? host, int width = 720, int height = 480)
        {
            if (sample != null) publish.Invoke(vm, [sample, host]);
            render(file, width, height);
            observations.Add(new { Image = file, SyntheticObservation = kind, HostProcessIdIsSynthetic = host != null,
                Sample = sample, Caption = vm.ResourceStatsText, Tooltip = vm.ResourceStatsDetails,
                Layout = InspectResourceStatsLayout(window).Observation });
            WriteEvidence();
        }
        try
        {
            vm.Settings.ShowResourceUsage = true;
            vm.RefreshResourceUsagePreferences();
            Invoke("CloseEditorContext");
            setProfile.Invoke(vm, [null]);
            vm.StatusDetails = "Synthetic resource display · no capture or process sampling";
            typeof(MainWindow).GetField("_layoutMode", Private)!.SetValue(window, WorkspaceLayoutMode.Compact);
            Invoke("ShowCompactEditor");
            // Preserve the real constructor's not-yet-sampled presentation.
            Frame("77-resources-not-sampled-minimum.png", "Production initial state; timer remains stopped", null, null);
            Frame("78-resources-idle-minimum.png", "Only Flick app metrics; recorder absent", new ResourceSample {
                AppPrivateBytes = 128 * mib, AppCpuPercent = 1.2, HasAppSample = true, HasAppCpuSample = true }, null);
            var active = new ResourceSample { AppPrivateBytes = 128 * mib, CaptureHostPrivateBytes = 512 * mib,
                AppCpuPercent = 2.3, CaptureHostCpuPercent = 17.6, HasAppSample = true, HasCaptureHostSample = true,
                HasAppCpuSample = true, HasCaptureHostCpuSample = true };
            Frame("79-resources-combined-minimum.png", "Synthetic concurrent app and recorder metrics", active, 42424);
            Frame("80-resources-combined-wide.png", "Same synthetic concurrent metrics at standard size", active, 42424, 1280, 720);
            Frame("81-resources-unavailable-minimum.png", "Recorder requested but measurements unavailable", new ResourceSample {
                AppPrivateBytes = 128 * mib, AppCpuPercent = 2.3, HasAppSample = true, HasAppCpuSample = true }, 42424);
            Frame("82-resources-cpu-pending-minimum.png", "Memory measured; CPU first-interval baseline pending", new ResourceSample {
                AppPrivateBytes = 128 * mib, CaptureHostPrivateBytes = 512 * mib,
                HasAppSample = true, HasCaptureHostSample = true }, 42424);
            var high = new ResourceSample { AppPrivateBytes = 8192 * mib, CaptureHostPrivateBytes = 16384 * mib,
                AppCpuPercent = 34.5, CaptureHostCpuPercent = 65.4, HasAppSample = true, HasCaptureHostSample = true,
                HasAppCpuSample = true, HasCaptureHostCpuSample = true };
            Frame("83-resources-large-values-minimum.png", "Large synthetic values test labels, not resource performance", high, 42424);
            vm.Settings.ShowResourceUsage = false;
            vm.RefreshResourceUsagePreferences();
            Frame("85-resources-disabled-minimum.png", "Actual display preference disabled; no reads requested", null, null);
            if (vm.ResourceStatsVisible) throw new InvalidOperationException("The disabled preference must hide the resource caption.");
            vm.Settings.ShowResourceUsage = true;
            vm.RefreshResourceUsagePreferences();
            Frame("86-resources-enabled-minimum.png", "Actual preference re-enabled; waiting for the first sample", null, null);

            Invoke("OpenSettings");
            resourcePage = (SettingsWindow)typeof(MainWindow).GetField("_settingsPage", Private)!.GetValue(window)!;
            typeof(SettingsWindow).GetMethod("SelectCategory", Private)!.Invoke(resourcePage, ["General"]);
            var displayToggle = (CheckBox)resourcePage.FindName("ChkShowResourceUsage");
            if (displayToggle.IsChecked != true) throw new InvalidOperationException("General Display must reflect the enabled preference.");
            Frame("87-settings-resource-display-wide.png", "Actual General Display toggle, no Save invoked", null, null, 1280, 720);
            displayToggle.IsChecked = false;
            var general = (FrameworkElement)resourcePage.FindName("PanelGeneral");
            ((ScrollViewer)resourcePage.FindName("SettingsScroll")).ScrollToVerticalOffset(
                Math.Max(0, displayToggle.TranslatePoint(new Point(0, -60), general).Y));
            Frame("88-settings-resource-display-staged-minimum.png", "General Display change staged only; live footer remains enabled", null, null);
            Rect toggleBounds = displayToggle.TransformToAncestor(resourcePage).TransformBounds(new Rect(displayToggle.RenderSize));
            if (!ProductContains(new Rect(resourcePage.RenderSize), toggleBounds) || !vm.Settings.ShowResourceUsage || !vm.ResourceStatsVisible)
                throw new InvalidOperationException("The staged display toggle is clipped or applied without Save.");
            Invoke("ReturnToClipWorkspace");
            resourcePage = null;
            setProfile.Invoke(vm, [new CaptureEncodingProfile("obs_x264", "CPU x264 H.264", true, 1280, 720, 30)]);
            Invoke("EditorTool_Click", window.FindName("ToolShare"), new RoutedEventArgs());
            PumpUntil(() => !vm.IsBackgroundWorkActive && typeof(MainViewModel).GetField("_sharePreviewCancellation", Private)!.GetValue(vm) == null, window.Dispatcher);
            if (!vm.TryGetReadyShareFile(out _) || string.IsNullOrWhiteSpace(vm.SoftwareFallbackWarning))
                throw new InvalidOperationException("Combined resource/CPU-warning fixture requires genuine Ready media and the real warning.");
            Frame("84-resources-share-cpu-warning-minimum.png", "Large synthetic resources with genuine Share MP4 and simulated CPU profile", high, 42424);
            if (vm.CaptureHost.IsRunning || vm.DiagnosticsInitialized)
                throw new InvalidOperationException("Resource fixtures must never start a host or journal.");
            if (unexpectedQueries != 0 || !ReferenceEquals(originalRefresh, typeof(MainViewModel).GetField("_resourceRefreshTask", Private)!.GetValue(vm)))
                throw new InvalidOperationException("Changing resource preferences launched a process query in a rendering fixture.");
        }
        finally
        {
            if (resourcePage != null && ReferenceEquals(resourcePage, typeof(MainWindow).GetField("_settingsPage", Private)!.GetValue(window)))
                Invoke("ReturnToClipWorkspace");
            vm.Settings.ShowResourceUsage = originalPreference;
            vm.RefreshResourceUsagePreferences();
            samplerField.SetValue(vm, originalSampler);
            vm.ResourceStatsText = originalText;
            vm.ResourceStatsDetails = originalDetails;
            vm.StatusDetails = originalStatus;
            setProfile.Invoke(vm, [originalProfile]);
            typeof(MainWindow).GetField("_layoutMode", Private)!.SetValue(window, originalMode);
            Invoke("ApplyWorkspaceLayout");
            restored = vm.ResourceStatsText == originalText && vm.ResourceStatsDetails == originalDetails &&
                vm.Settings.ShowResourceUsage == originalPreference && vm.ResourceStatsVisible == originalPreference &&
                originalPreferences.SequenceEqual(File.ReadAllBytes(LocalClip.Core.Models.AppSettings.GetSettingsFilePath()));
            WriteEvidence();
            if (!restored) throw new InvalidOperationException("Resource fixtures did not restore the display state and unchanged preferences.");
        }

        void WriteEvidence() => File.WriteAllText(Path.Combine(output, "resource-fixture-evidence.json"), JsonSerializer.Serialize(new {
            Scope = "Actual production resource projection and WPF bindings; deterministic in-memory samples only. No polling, native process readings, capture, helper or performance measurement.",
            TimerStopped = timerStopped, SimulatedRecorderPid = 42424, ValuesAreNotMeasuredPerformance = true,
            ActualProcessQueriesRequested = unexpectedQueries, SettingsSaved = false,
            OriginalResourcePropertiesRestoredAfterFixtures = restored, Observations = observations
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static (bool Fits, object Observation) InspectResourceStatsLayout(MainWindow window)
    {
        var vm = (MainViewModel)window.DataContext;
        var caption = (TextBlock)window.FindName("TxtResourceStats");
        var footer = (Border)window.FindName("AppStatusBar");
        var root = (FrameworkElement)window.Content;
        string tooltip = Convert.ToString(caption.ToolTip) ?? "";
        bool bindingsMatch = caption.Text == vm.ResourceStatsText && tooltip == vm.ResourceStatsDetails;
        if (!vm.ResourceStatsVisible)
        {
            bool collapsed = caption.Visibility == Visibility.Collapsed;
            return (collapsed && bindingsMatch, new { Caption = caption.Text, Tooltip = tooltip,
                Enabled = false, Collapsed = collapsed, BindingsMatch = bindingsMatch });
        }
        Rect bounds = caption.TransformToAncestor(root).TransformBounds(new Rect(caption.RenderSize));
        Rect footerBounds = footer.TransformToAncestor(root).TransformBounds(new Rect(footer.RenderSize));
        var natural = new FormattedText(caption.Text, CultureInfo.CurrentUICulture, caption.FlowDirection,
            new Typeface(caption.FontFamily, caption.FontStyle, caption.FontWeight, caption.FontStretch), caption.FontSize,
            caption.Foreground ?? Brushes.White, VisualTreeHelper.GetDpi(caption).PixelsPerDip);
        if (!double.IsNaN(caption.LineHeight)) natural.LineHeight = caption.LineHeight;
        bool visible = caption.Visibility == Visibility.Visible && footer.Visibility == Visibility.Visible;
        bool contained = bounds.Width > 0 && bounds.Height > 0 && bounds.Left >= footerBounds.Left - 1 &&
            bounds.Top >= footerBounds.Top - 1 && bounds.Right <= footerBounds.Right + 1 && bounds.Bottom <= footerBounds.Bottom + 1;
        bool textFits = caption.ActualWidth + 1 >= natural.WidthIncludingTrailingWhitespace && caption.ActualHeight + 1 >= natural.Height &&
            caption.TextTrimming == TextTrimming.None && caption.TextWrapping == TextWrapping.NoWrap;
        bool meaning = tooltip.Contains("CPU", StringComparison.OrdinalIgnoreCase) && tooltip.Contains("RAM", StringComparison.OrdinalIgnoreCase) &&
            tooltip.Contains("Flick", StringComparison.OrdinalIgnoreCase);
        return (visible && contained && textFits && meaning && bindingsMatch, new {
            Caption = caption.Text, Tooltip = tooltip, Visible = visible, Contained = contained, TextFits = textFits,
            TooltipNamesCpuRamAndFlick = meaning, BindingsMatch = bindingsMatch,
            caption.ActualWidth, caption.ActualHeight, caption.FontSize,
            NaturalTextWidth = natural.WidthIncludingTrailingWhitespace, NaturalTextHeight = natural.Height,
            FooterHeight = footer.ActualHeight });
    }
}
