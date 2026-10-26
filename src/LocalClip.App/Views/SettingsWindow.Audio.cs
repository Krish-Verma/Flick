using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using LocalClip.Core.Audio;
using LocalClip.Core.Models;

namespace LocalClip.App.Views;

public partial class SettingsWindow
{
    private readonly Func<Task<TargetList>>? _loadAudioSources;
    private readonly Func<AudioEndpointFlow, IReadOnlyList<AudioEndpoint>> _loadAudioEndpoints;
    private IReadOnlyList<AudioEndpoint> _availableOutputs = Array.Empty<AudioEndpoint>();
    private IReadOnlyList<AudioEndpoint> _availableInputs = Array.Empty<AudioEndpoint>();
    private bool _audioDevicesLoaded;
    private int _audioDevicesEpoch;

    private sealed record AudioChoice(AudioProcessIdentity? Identity)
    {
        public string Label => Identity?.DisplayLabel ?? "None";
    }

    private void AudioMode_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (AppAudioSourcesPanel != null)
            AppAudioSourcesPanel.Visibility = CmbAudioMode.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        if (SystemAudioDevicePanel != null)
            SystemAudioDevicePanel.IsEnabled = CmbAudioMode.SelectedIndex != 1;
    }

    private void PopulateAudio(AppSettings settings)
    {
        CmbAudioMode.SelectedIndex = AudioCapturePolicy.IsSeparated(settings) ? 1 : 0;
        CmbGameAudio.ItemsSource = new[] { new AudioChoice(null), new AudioChoice(settings.GameAudioSource) }.Distinct().ToArray();
        CmbVoiceAudio.ItemsSource = new[] { new AudioChoice(null), new AudioChoice(settings.VoiceAudioSource) }.Distinct().ToArray();
        CmbGameAudio.SelectedIndex = settings.GameAudioSource == null ? 0 : 1;
        CmbVoiceAudio.SelectedIndex = settings.VoiceAudioSource == null ? 0 : 1;
        PopulateAudioEndpointChoices(settings.SystemAudioDeviceId, settings.MicrophoneDeviceId);
        AudioMode_SelectionChanged(this, null!);
    }

    private void PopulateAudioEndpointChoices(string? outputId, string? inputId)
    {
        var outputs = AudioEndpointChoices.ForSelection(AudioEndpointFlow.Output, outputId, _availableOutputs);
        var inputs = AudioEndpointChoices.ForSelection(AudioEndpointFlow.Input, inputId, _availableInputs);
        CmbSystemAudioDevice.ItemsSource = outputs;
        CmbSystemAudioDevice.SelectedItem = outputs.FirstOrDefault(c => string.Equals(c.Id, outputId, StringComparison.OrdinalIgnoreCase))
            ?? outputs[0];
        CmbMicrophoneDevice.ItemsSource = inputs;
        CmbMicrophoneDevice.SelectedItem = inputs.FirstOrDefault(c => string.Equals(c.Id, inputId, StringComparison.OrdinalIgnoreCase))
            ?? inputs[0];
    }

    private async void AudioPanel_Loaded(object sender, RoutedEventArgs e)
    {
        if (_audioDevicesLoaded) return;
        _audioDevicesLoaded = true;
        await RefreshAudioDevicesAsync();
    }

    private async void RefreshAudioDevices_Click(object sender, RoutedEventArgs e) =>
        await RefreshAudioDevicesAsync();

    private async Task RefreshAudioDevicesAsync()
    {
        if (!BtnRefreshAudioDevices.IsEnabled) return;
        int epoch = ++_audioDevicesEpoch;
        BtnRefreshAudioDevices.IsEnabled = false;
        TxtAudioDeviceStatus.Text = "Finding devices…";
        try
        {
            // COM enumeration is read-only, and runs off the WPF/gameplay UI
            // thread. It never opens a microphone or starts the OBS helper.
            var found = await Task.Run(() => (
                Outputs: _loadAudioEndpoints(AudioEndpointFlow.Output),
                Inputs: _loadAudioEndpoints(AudioEndpointFlow.Input)));
            if (epoch != _audioDevicesEpoch) return; // Staged Settings page was closed/replaced.
            // A user can change either picker while discovery is in flight.
            // Read the current choices only after it settles so the refresh
            // cannot restore an old selection over those staged edits.
            string selectedOutput = (CmbSystemAudioDevice.SelectedItem as AudioEndpointChoice)?.Id ?? _settings.SystemAudioDeviceId;
            string selectedInput = (CmbMicrophoneDevice.SelectedItem as AudioEndpointChoice)?.Id ?? _settings.MicrophoneDeviceId;
            _availableOutputs = found.Outputs;
            _availableInputs = found.Inputs;
            PopulateAudioEndpointChoices(selectedOutput, selectedInput);
            TxtAudioDeviceStatus.Text = $"{_availableOutputs.Count} outputs · {_availableInputs.Count} microphones";
            TxtAudioDeviceStatus.ToolTip = "Windows defaults follow system changes. Unavailable selections are kept and never silently replaced.";
        }
        catch (Exception)
        {
            if (epoch == _audioDevicesEpoch)
            {
                TxtAudioDeviceStatus.Text = "Couldn't refresh devices. Your choices are kept.";
                TxtAudioDeviceStatus.ToolTip = "Try Refresh again or explicitly choose a Windows default device.";
            }
        }
        finally
        {
            if (epoch == _audioDevicesEpoch)
                BtnRefreshAudioDevices.IsEnabled = true;
        }
    }

    private async void RefreshAudioSources_Click(object sender, RoutedEventArgs e) =>
        await RefreshAudioSourcesAsync();

    private async Task RefreshAudioSourcesAsync()
    {
        if (_loadAudioSources == null || _isCaptureActive())
        {
            TxtAudioSources.Text = "Stop replay before refreshing app audio sources."; return;
        }
        BtnRefreshAudioSources.IsEnabled = false;
        try
        {
            var targets = await _loadAudioSources();
            // Preserve selections made while the helper was discovering apps.
            var oldGame = (CmbGameAudio.SelectedItem as AudioChoice)?.Identity;
            var oldVoice = (CmbVoiceAudio.SelectedItem as AudioChoice)?.Identity;
            var choices = new List<AudioChoice> { new(null) };
            choices.AddRange(targets.Windows.Where(window => AudioProcessIdentity.FromWindow(window).IsComplete)
                .GroupBy(window => window.Pid).Select(group => new AudioChoice(AudioProcessIdentity.FromWindow(group.First()))));
            CmbGameAudio.ItemsSource = choices; CmbVoiceAudio.ItemsSource = choices;
            CmbGameAudio.SelectedItem = choices.FirstOrDefault(choice => Same(choice.Identity, oldGame)) ?? choices[0];
            CmbVoiceAudio.SelectedItem = choices.FirstOrDefault(choice => Same(choice.Identity, oldVoice)) ?? choices[0];
            TxtAudioSources.Text = "Apps refreshed. Choose what to record.";
            TxtAudioSources.ToolTip = "Capture includes each selected process tree. Select the app again after a restart; audio activity appears in Recording Health.";
        }
        catch (Exception) { TxtAudioSources.Text = "App sources could not be refreshed. Stop replay and try again."; }
        finally { BtnRefreshAudioSources.IsEnabled = true; }
    }

    private static bool Same(AudioProcessIdentity? first, AudioProcessIdentity? second) => first == null ? second == null :
        second != null && first.ProcessId == second.ProcessId && first.CreationTime == second.CreationTime &&
        string.Equals(first.ExecutablePath, second.ExecutablePath, StringComparison.OrdinalIgnoreCase);

    private bool AudioRoutingChanged() =>
        (CmbAudioMode.SelectedIndex == 1 ? "Separated" : "SystemFallback") != _settings.AudioMode ||
        !Same((CmbGameAudio.SelectedItem as AudioChoice)?.Identity, _settings.GameAudioSource) ||
        !Same((CmbVoiceAudio.SelectedItem as AudioChoice)?.Identity, _settings.VoiceAudioSource) ||
        !string.Equals((CmbSystemAudioDevice.SelectedItem as AudioEndpointChoice)?.Id, _settings.SystemAudioDeviceId, StringComparison.OrdinalIgnoreCase) ||
        !string.Equals((CmbMicrophoneDevice.SelectedItem as AudioEndpointChoice)?.Id, _settings.MicrophoneDeviceId, StringComparison.OrdinalIgnoreCase) ||
        (ChkMicrophone.IsChecked == true) != _settings.EnableMicrophone;

    private void StageAudio(AppSettings staged)
    {
        staged.AudioMode = CmbAudioMode.SelectedIndex == 1 ? "Separated" : "SystemFallback";
        staged.GameAudioSource = (CmbGameAudio.SelectedItem as AudioChoice)?.Identity;
        staged.VoiceAudioSource = (CmbVoiceAudio.SelectedItem as AudioChoice)?.Identity;
        var output = CmbSystemAudioDevice.SelectedItem as AudioEndpointChoice;
        var input = CmbMicrophoneDevice.SelectedItem as AudioEndpointChoice;
        if (output == null || input == null)
            throw new ArgumentException("Choose valid output and microphone preferences under Audio settings.");
        // Discovery is asynchronous, and remembered USB/headset IDs may be
        // disconnected. Keeping an unchanged preference must not block Save
        // for unrelated settings, or silently replace it with Windows default.
        // A newly selected/enabled source still needs an available device.
        bool outputChanged = !string.Equals(output.Id, _settings.SystemAudioDeviceId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(_settings.AudioMode, "SystemFallback", StringComparison.OrdinalIgnoreCase);
        bool microphoneChanged = !string.Equals(input.Id, _settings.MicrophoneDeviceId, StringComparison.OrdinalIgnoreCase) ||
            !_settings.EnableMicrophone;
        if (staged.AudioMode == "SystemFallback" && !output.IsAvailable && outputChanged)
            throw new ArgumentException("Choose an available audio output in Audio settings.");
        if (ChkMicrophone.IsChecked == true && !input.IsAvailable && microphoneChanged)
            throw new ArgumentException("Choose an available microphone in Audio settings.");
        staged.SystemAudioDeviceId = output.Id;
        staged.MicrophoneDeviceId = input.Id;
        AudioCapturePolicy.Validate(staged);
    }
}
