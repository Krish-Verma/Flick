using System.ComponentModel;
using System.Reflection;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using LocalClip.App.Services;
using LocalClip.App.ViewModels;
using LocalClip.App.Views;
using LocalClip.App.Views.Components;
using LocalClip.Core.Audio;
using LocalClip.Core.Models;
using LocalClip.Testing;
using Xunit;

namespace LocalClip.App.Tests;

internal static class BugAuditUiAssertions
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    public static void Run()
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Application.Current.Dispatcher));
        try
        {
            CanceledTrimDoesNotCommit();
            RefreshReturnsAfterPageInvalidation();
            PreviewOriginAndPromptDefault();
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }

    private static void CanceledTrimDoesNotCommit()
    {
        var range = new TrimRangeControl { Width = 420 };
        range.SetRange(10, 1, 8);
        range.Measure(new Size(420, 180)); range.Arrange(new Rect(0, 0, 420, 180)); range.UpdateLayout();
        int commits = 0, reversions = 0;
        range.RangeCommitted += (_, _) => commits++;
        range.RangeReverted += (_, _) => reversions++;
        var handle = (Thumb)range.FindName("StartHandle");
        Invoke(range, "StartHandle_DragDelta", handle, new DragDeltaEventArgs(30, 0));
        Assert.NotEqual(1, range.SelectionStart);
        Invoke(range, "Handle_DragCompleted", handle, new DragCompletedEventArgs(30, 0, true));
        Assert.Equal(1, range.SelectionStart); Assert.Equal(8, range.SelectionEnd);
        Assert.Equal(0, commits); Assert.Equal(1, reversions);
        Invoke(range, "StartHandle_DragDelta", handle, new DragDeltaEventArgs(30, 0));
        Invoke(range, "Handle_DragCompleted", handle, new DragCompletedEventArgs(30, 0, false));
        Assert.Equal(1, commits);
    }

    private static void RefreshReturnsAfterPageInvalidation()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        int reads = 0;
        var page = new SettingsWindow(new AppSettings(), () => false,
            loadAudioEndpoints: _ => {
                Interlocked.Increment(ref reads); entered.Set();
                if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Owned device read was not released.");
                return Array.Empty<AudioEndpoint>();
            }, saveReviewedSettings: _ => throw new InvalidOperationException("No profile write in fixture"),
            applyWindowsStartup: (_, _) => throw new InvalidOperationException("No startup write in fixture"),
            readWindowsStartupStatus: _ => new WindowsStartupStatus(WindowsStartupRegistrationState.Registered, "Owned fixture"));
        try
        {
            var work = (Task)Invoke(page, "RefreshAudioDevicesAsync")!;
            Assert.True(entered.Wait(TimeSpan.FromSeconds(2)));
            Assert.False(((Button)page.FindName("BtnRefreshAudioDevices")).IsEnabled);
            page.LeavePage(); // Manage folders retains and later reattaches this exact page.
            release.Set(); Pump(work);
            Assert.True(((Button)page.FindName("BtnRefreshAudioDevices")).IsEnabled);
            int before = reads;
            Pump((Task)Invoke(page, "RefreshAudioDevicesAsync")!);
            Assert.True(reads > before);
        }
        finally { release.Set(); page.LeavePage(); }
    }

    private static void PreviewOriginAndPromptDefault()
    {
        using var owned = new CaptureHostPipeFixture(); // Never launched.
        var settings = new AppSettings { StorageRoot = owned.Root,
            OriginalsFolder = System.IO.Path.Combine(owned.Root, "Originals"),
            ExportsFolder = System.IO.Path.Combine(owned.Root, "Exports"), TempFolder = System.IO.Path.Combine(owned.Root, "Temp") };
        using var vm = (MainViewModel)typeof(MainViewModel).GetConstructor(Hidden, null,
            [typeof(AppSettings), typeof(string)], null)!.Invoke([settings, System.IO.Path.Combine(owned.Root, "Metadata")]);
        var window = (MainWindow)typeof(MainWindow).GetConstructor(Hidden, null, [typeof(MainViewModel)], null)!.Invoke([vm]); // Unshown; no native registration or device discovery.
        var app = Application.Current; var previousMain = app.MainWindow;
        try
        {
            app.MainWindow = window;
            var clip = new ClipMetadata { DurationSeconds = 30, TrimStartSeconds = 10, TrimEndSeconds = 20 };
            vm.CaptureStatus = "Starting"; // Prevent optional thumbnail work in this metadata-only fixture.
            vm.SelectedClip = clip;
            var slider = (Slider)window.FindName("PlayerSlider"); slider.Maximum = 30; slider.Value = 5;
            var mode = typeof(MainWindow).GetField("_playerView", Hidden)!;
            typeof(MainWindow).GetField("_previewOriginalStartSeconds", Hidden)!.SetValue(window, 10d);
            foreach (string name in new[] { "AudioMix", "Exported" })
            {
                mode.SetValue(window, Enum.Parse(mode.FieldType, name));
                Assert.Equal(15d, (double)typeof(MainWindow).GetProperty("OriginalPlayhead", Hidden)!.GetValue(window)!);
                clip.TrimStartSeconds = 12; // The already-built preview still starts at original second 10.
                Assert.Equal(15d, (double)typeof(MainWindow).GetProperty("OriginalPlayhead", Hidden)!.GetValue(window)!);
            }
            mode.SetValue(window, Enum.Parse(mode.FieldType, "Original"));
            Assert.Equal(5d, (double)typeof(MainWindow).GetProperty("OriginalPlayhead", Hidden)!.GetValue(window)!);
            Assert.Null(((MediaElement)window.FindName("VideoPlayer")).Source);

            Exception? failure = null;
            app.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => {
                try
                {
                    var buttons = Descendants(((ContentControl)window.FindName("DialogContentHost")).Content).OfType<Button>().ToArray();
                    var save = Assert.Single(buttons, b => Equals(b.Content, "Save"));
                    Assert.True(save.IsDefault);
                    var input = Assert.Single(Descendants(((ContentControl)window.FindName("DialogContentHost")).Content).OfType<TextBox>());
                    input.Text = "New title";
                    ((IInvokeProvider)new ButtonAutomationPeer(save).GetPattern(PatternInterface.Invoke)!).Invoke();
                }
                catch (Exception error)
                {
                    failure = error;
                    var cancel = Descendants(((ContentControl)window.FindName("DialogContentHost")).Content).OfType<Button>().FirstOrDefault(b => Equals(b.Content, "Cancel"));
                    if (cancel != null)
                        ((IInvokeProvider)new ButtonAutomationPeer(cancel).GetPattern(PatternInterface.Invoke)!).Invoke();
                    else
                    {
                        window.Closing -= typeof(MainWindow).GetMethod("Window_Closing", Hidden)!.CreateDelegate<CancelEventHandler>(window);
                        window.Close(); // Closed releases the production dialog's dispatcher frame.
                    }
                }
            }));
            Assert.Equal("New title", FlickDialog.Prompt("Rename clip", "Enter a clip title", "Old title"));
            if (failure != null) throw failure;
        }
        finally
        {
            app.MainWindow = previousMain;
            window.Closing -= typeof(MainWindow).GetMethod("Window_Closing", Hidden)!.CreateDelegate<CancelEventHandler>(window);
            window.Close();
        }
    }

    private static object? Invoke(object target, string name, params object?[] args) =>
        target.GetType().GetMethod(name, Hidden)!.Invoke(target, args);
    private static IEnumerable<DependencyObject> Descendants(object? root)
    {
        if (root is not DependencyObject value) yield break;
        yield return value;
        foreach (object child in LogicalTreeHelper.GetChildren(value))
            foreach (var item in Descendants(child)) yield return item;
    }
    private static void Pump(Task task)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (!task.IsCompleted)
        {
            if (timer.Elapsed > TimeSpan.FromSeconds(8)) throw new TimeoutException("Owned UI operation did not settle.");
            Application.Current.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            Thread.Sleep(1);
        }
        task.GetAwaiter().GetResult();
    }
}
