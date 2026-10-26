using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using LocalClip.App.Views;
using LocalClip.Core.Audio;
using LocalClip.Core.Models;
using Xunit;

namespace LocalClip.App.Tests;

// Uses the suite's existing STA/Application. All device observations and file
// paths are test-owned; this never enumerates real audio, touches HKCU or opens
// a microphone/recording helper.
internal static class SettingsAudioPersistenceAssertions
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

    public static void Run(string root)
    {
        RememberedDisconnectedDevicesDoNotBlockUnrelatedSave(root);
        DiscoveryDoesNotOverwriteNewerSelections(root);
        AppDiscoveryDoesNotOverwriteNewerSelections(root);
        NewlyEnabledUnavailableDevicesStillRequireAnExplicitChoice(root);
        InvalidInputAndWriteFailuresKeepTheDraft(root);
    }

    private static void RememberedDisconnectedDevicesDoNotBlockUnrelatedSave(string root)
    {
        var source = Preferences(root);
        source.SystemAudioDeviceId = "remembered-usb-output";
        source.MicrophoneDeviceId = "remembered-usb-microphone";
        source.EnableMicrophone = true; // Existing opt-in, not enabled by this Save.
        string file = Path.Combine(root, "disconnected-audio.json");
        var page = Page(source, file, _ => Array.Empty<AudioEndpoint>());
        Assert.False(Choice(page, "CmbSystemAudioDevice").IsAvailable);
        Assert.False(Choice(page, "CmbMicrophoneDevice").IsAvailable);

        // Before the first discovery result, a remembered non-default ID is
        // unavailable in the picker. The old Save path rejected this exact case.
        StageAudio(page, AppSettings.CreateDefault());
        Find<CheckBox>(page, "ChkShowResourceUsage").IsChecked = false;
        bool saved = false;
        page.CloseRequested += complete => saved = complete;
        Invoke(page, "Save_Click", page, new RoutedEventArgs());
        Assert.True(saved);
        var disk = AppSettings.LoadExistingFile(file);
        Assert.False(disk.ShowResourceUsage);
        Assert.Equal("remembered-usb-output", disk.SystemAudioDeviceId);
        Assert.Equal("remembered-usb-microphone", disk.MicrophoneDeviceId);
        Assert.True(disk.EnableMicrophone);

        // After a complete discovery with the devices still unplugged, Save
        // must also preserve their exact IDs instead of requiring a reset.
        var reopened = Page(disk, file, _ => Array.Empty<AudioEndpoint>());
        Refresh(reopened);
        Assert.False(Choice(reopened, "CmbSystemAudioDevice").IsAvailable);
        Assert.False(Choice(reopened, "CmbMicrophoneDevice").IsAvailable);
        StageAudio(reopened, AppSettings.CreateDefault());
        Find<CheckBox>(reopened, "ChkShowResourceUsage").IsChecked = true;
        saved = false;
        reopened.CloseRequested += complete => saved = complete;
        Invoke(reopened, "Save_Click", reopened, new RoutedEventArgs());
        Assert.True(saved);
        disk = AppSettings.LoadExistingFile(file);
        Assert.True(disk.ShowResourceUsage);
        Assert.Equal(source.SystemAudioDeviceId, disk.SystemAudioDeviceId);
        Assert.Equal(source.MicrophoneDeviceId, disk.MicrophoneDeviceId);
        Assert.True(disk.EnableMicrophone);
    }

    private static void DiscoveryDoesNotOverwriteNewerSelections(string root)
    {
        var source = Preferences(root);
        string file = Path.Combine(root, "audio-discovery-race.json");
        using var entered = new ManualResetEventSlim();
        using var complete = new ManualResetEventSlim();
        AudioEndpoint[] outputs = [new("chosen-output", "Chosen headphones", false)];
        AudioEndpoint[] inputs = [new("chosen-input", "Chosen microphone", false)];
        var page = Page(source, file, flow =>
        {
            entered.Set();
            if (!complete.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Synthetic discovery did not settle.");
            return flow == AudioEndpointFlow.Output ? outputs : inputs;
        });
        SetChoices(page, "CmbSystemAudioDevice", AudioEndpointFlow.Output, outputs);
        SetChoices(page, "CmbMicrophoneDevice", AudioEndpointFlow.Input, inputs);
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(page.Dispatcher));
        try
        {
            Task refresh = (Task)Invoke(page, "RefreshAudioDevicesAsync")!;
            Assert.True(entered.Wait(TimeSpan.FromSeconds(2)));
            Assert.False(refresh.IsCompleted);
            // The user changes both picks while a read-only refresh is pending.
            Find<ComboBox>(page, "CmbSystemAudioDevice").SelectedIndex = 1;
            Find<ComboBox>(page, "CmbMicrophoneDevice").SelectedIndex = 1;
            complete.Set();
            Pump(refresh);
            Assert.Equal("chosen-output", Choice(page, "CmbSystemAudioDevice").Id);
            Assert.Equal("chosen-input", Choice(page, "CmbMicrophoneDevice").Id);
            Find<CheckBox>(page, "ChkMicrophone").IsChecked = true;
            bool saved = false;
            page.CloseRequested += value => saved = value;
            Invoke(page, "Save_Click", page, new RoutedEventArgs());
            Assert.True(saved);
            var disk = AppSettings.LoadExistingFile(file);
            Assert.Equal("chosen-output", disk.SystemAudioDeviceId);
            Assert.Equal("chosen-input", disk.MicrophoneDeviceId);
            Assert.True(disk.EnableMicrophone);
        }
        finally
        {
            complete.Set();
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }
    }

    private static void NewlyEnabledUnavailableDevicesStillRequireAnExplicitChoice(string root)
    {
        var source = Preferences(root);
        string file = Path.Combine(root, "invalid-audio-choice.json");
        var page = Page(source, file, _ => Array.Empty<AudioEndpoint>());
        var output = Find<ComboBox>(page, "CmbSystemAudioDevice");
        output.ItemsSource = new[] { new AudioEndpointChoice("new-unavailable-output", "Unavailable", false) };
        output.SelectedIndex = 0;
        var failure = Assert.Throws<TargetInvocationException>(() => StageAudio(page, AppSettings.CreateDefault()));
        Assert.Contains("available audio output", Assert.IsType<ArgumentException>(failure.InnerException).Message);
        Assert.Equal("default", source.SystemAudioDeviceId);
        Assert.False(File.Exists(file));
        bool closed = false;
        page.CloseRequested += _ => closed = true;
        Invoke(page, "Save_Click", page, new RoutedEventArgs());
        Assert.False(closed);
        Assert.Equal(Visibility.Visible, Find<StackPanel>(page, "PanelAudio").Visibility);
        Assert.Contains("available audio output", Find<TextBlock>(page, "TxtSaveStatus").Text);
        Assert.False(File.Exists(file));

        source.MicrophoneDeviceId = "remembered-but-off";
        page = Page(source, file, _ => Array.Empty<AudioEndpoint>());
        Assert.False(Choice(page, "CmbMicrophoneDevice").IsAvailable);
        Find<CheckBox>(page, "ChkMicrophone").IsChecked = true;
        failure = Assert.Throws<TargetInvocationException>(() => StageAudio(page, AppSettings.CreateDefault()));
        Assert.Contains("available microphone", Assert.IsType<ArgumentException>(failure.InnerException).Message);
        Assert.False(source.EnableMicrophone);
        Assert.False(File.Exists(file));
    }

    private static void AppDiscoveryDoesNotOverwriteNewerSelections(string root)
    {
        var source = Preferences(root);
        string file = Path.Combine(root, "app-discovery-race.json");
        var completion = new TaskCompletionSource<TargetList>(TaskCreationOptions.RunContinuationsAsynchronously);
        var target = new TargetWindow { Pid = 42, Hwnd = 5, ProcessCreationFileTime = 123,
            ExecutablePath = @"C:\Synthetic\chosen-game.exe", Exe = "chosen-game.exe", Title = "Chosen game" };
        var choice = AudioProcessIdentity.FromWindow(target);
        var page = Page(source, file, _ => Array.Empty<AudioEndpoint>(), () => completion.Task);
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(page.Dispatcher));
        try
        {
            Task refresh = (Task)Invoke(page, "RefreshAudioSourcesAsync")!;
            Assert.False(refresh.IsCompleted);
            // Seed an available choice via the production picker population
            // path, then let the user's new selection race the pending scan.
            Invoke(page, "PopulateAudio", new AppSettings { AudioMode = "Separated", GameAudioSource = choice });
            completion.SetResult(new TargetList { Windows = [target] });
            Pump(refresh);
            var staged = AppSettings.CreateDefault();
            StageAudio(page, staged);
            Assert.Equal(choice, staged.GameAudioSource);
            Assert.Equal("Separated", staged.AudioMode);
            bool saved = false;
            page.CloseRequested += value => saved = value;
            Invoke(page, "Save_Click", page, new RoutedEventArgs());
            Assert.True(saved);
            Assert.Equal(choice, AppSettings.LoadExistingFile(file).GameAudioSource);
        }
        finally
        {
            completion.TrySetResult(new TargetList());
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }
    }

    private static void InvalidInputAndWriteFailuresKeepTheDraft(string root)
    {
        var source = Preferences(root);
        var page = Page(source, Path.Combine(root, "invalid-duration.json"), _ => Array.Empty<AudioEndpoint>());
        Find<TextBox>(page, "TxtDuration").Text = "0";
        bool closed = false;
        page.CloseRequested += _ => closed = true;
        Invoke(page, "Save_Click", page, new RoutedEventArgs());
        Assert.False(closed);
        Assert.Equal(30, source.ReplayDurationSeconds);
        Assert.Equal("0", Find<TextBox>(page, "TxtDuration").Text);
        Assert.Equal("Choose 15–120 seconds.", Find<TextBlock>(page, "TxtSaveStatus").Text);

        Find<TextBox>(page, "TxtDuration").Text = "30";
        Find<CheckBox>(page, "ChkGameDetectionEnabled").IsChecked = true;
        Invoke(page, "Save_Click", page, new RoutedEventArgs());
        Assert.False(closed);
        Assert.False(source.GameDetectionEnabled);
        Assert.Equal(Visibility.Visible, Find<StackPanel>(page, "PanelGames").Visibility);
        Assert.Equal("Review your game settings.", Find<TextBlock>(page, "TxtSaveStatus").Text);

        string file = Path.Combine(root, "write-failure.json");
        source.SaveReviewedToFile(file);
        string before = File.ReadAllText(file);
        page = new SettingsWindow(source, () => false,
            loadAudioEndpoints: _ => Array.Empty<AudioEndpoint>(),
            saveReviewedSettings: _ => throw new IOException("Synthetic denied settings write"),
            applyWindowsStartup: (_, _) => throw new InvalidOperationException("No startup operation expected."));
        Find<CheckBox>(page, "ChkShowResourceUsage").IsChecked = false;
        closed = false;
        page.CloseRequested += _ => closed = true;
        Invoke(page, "Save_Click", page, new RoutedEventArgs());
        Assert.False(closed);
        Assert.True(source.ShowResourceUsage);
        Assert.Equal(before, File.ReadAllText(file));
        Assert.False(Find<CheckBox>(page, "ChkShowResourceUsage").IsChecked);
        Assert.Contains("changes are still here", Find<TextBlock>(page, "TxtSaveStatus").Text);
        Assert.Contains("Synthetic denied settings write", Find<TextBlock>(page, "TxtSaveStatus").ToolTip?.ToString());
    }

    private static AppSettings Preferences(string root)
    {
        var settings = AppSettings.CreateDefault();
        settings.OriginalsFolder = Path.Combine(root, "originals");
        settings.ExportsFolder = Path.Combine(root, "exports");
        return settings;
    }

    private static SettingsWindow Page(AppSettings settings, string file,
        Func<AudioEndpointFlow, IReadOnlyList<AudioEndpoint>> endpoints, Func<Task<TargetList>>? sources = null) =>
        new(settings, () => false, loadAudioSources: sources, loadAudioEndpoints: endpoints,
            saveReviewedSettings: staged => staged.SaveReviewedToFile(file),
            applyWindowsStartup: (_, _) => throw new InvalidOperationException("Unrelated Save must not touch Windows startup."));

    private static T Find<T>(SettingsWindow page, string name) where T : class =>
        Assert.IsType<T>(page.FindName(name));
    private static AudioEndpointChoice Choice(SettingsWindow page, string name) =>
        Assert.IsType<AudioEndpointChoice>(Find<ComboBox>(page, name).SelectedItem);
    private static object? Invoke(SettingsWindow page, string name, params object[] arguments) =>
        typeof(SettingsWindow).GetMethod(name, Hidden)!.Invoke(page, arguments);
    private static void StageAudio(SettingsWindow page, AppSettings settings) => Invoke(page, "StageAudio", settings);

    private static void SetChoices(SettingsWindow page, string name, AudioEndpointFlow flow, AudioEndpoint[] endpoints)
    {
        var picker = Find<ComboBox>(page, name);
        picker.ItemsSource = AudioEndpointChoices.ForSelection(flow, "default", endpoints);
        picker.SelectedIndex = 0;
    }

    private static void Refresh(SettingsWindow page)
    {
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(page.Dispatcher));
        try { Pump((Task)Invoke(page, "RefreshAudioDevicesAsync")!); }
        finally { SynchronizationContext.SetSynchronizationContext(previousContext); }
    }

    private static void Pump(Task task)
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        var frame = new DispatcherFrame();
        var guard = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        guard.Tick += (_, _) => frame.Continue = false;
        _ = task.ContinueWith(_ => dispatcher.BeginInvoke(new Action(() => frame.Continue = false)), TaskScheduler.Default);
        guard.Start();
        try { Dispatcher.PushFrame(frame); }
        finally { guard.Stop(); }
        Assert.True(task.IsCompleted, "Synthetic device discovery did not complete on the WPF dispatcher.");
        task.GetAwaiter().GetResult();
    }
}
