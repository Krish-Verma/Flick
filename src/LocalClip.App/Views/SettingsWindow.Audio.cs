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
        string selectedOutput = (CmbSystemAudioDevice.SelectedItem as AudioEndpointChoice)?.Id ?? _settings.SystemAudioDeviceId;
        string selectedInput = (CmbMicrophoneDevice.SelectedItem as AudioEndpointChoice)?.Id ?? _settings.MicrophoneDeviceId;
        BtnRefreshAudioDevices.IsEnabled = false;
        TxtAudioDeviceStatus.Text = "Looking for active Windows audio devices…";
        try
        {
            // COM enumeration is read-only, and runs off the WPF/gameplay UI
            // thread. It never opens a microphone or starts the OBS helper.
            var found = await Task.Run(() => (
                Outputs: _loadAudioEndpoints(AudioEndpointFlow.Output),
                Inputs: _loadAudioEndpoints(AudioEndpointFlow.Input)));
            if (epoch != _audioDevicesEpoch) return; // Staged Settings page was closed/replaced.
            _availableOutputs = found.Outputs;
            _availableInputs = found.Inputs;
            PopulateAudioEndpointChoices(selectedOutput, selectedInput);
            TxtAudioDeviceStatus.Text = $"Found {_availableOutputs.Count} playback and {_availableInputs.Count} microphone device(s). Windows defaults follow system changes. An unavailable selection will not silently switch devices.";
        }
        catch (Exception)
        {
            if (epoch == _audioDevicesEpoch)
                TxtAudioDeviceStatus.Text = "Could not enumerate audio devices. Your selected IDs were preserved. Refresh again, or choose the Windows defaults.";
        }
        finally
        {
            if (epoch == _audioDevicesEpoch)
                BtnRefreshAudioDevices.IsEnabled = true;
        }
    }

    private async void RefreshAudioSources_Click(object sender, RoutedEventArgs e)
    {
        if (_loadAudioSources == null || _isCaptureActive())
        {
            TxtAudioSources.Text = "Stop replay before refreshing app audio sources."; return;
        }
        BtnRefreshAudioSources.IsEnabled = false;
        try
        {
            var oldGame = (CmbGameAudio.SelectedItem as AudioChoice)?.Identity;
            var oldVoice = (CmbVoiceAudio.SelectedItem as AudioChoice)?.Identity;
            var targets = await _loadAudioSources();
            var choices = new List<AudioChoice> { new(null) };
            choices.AddRange(targets.Windows.Where(window => AudioProcessIdentity.FromWindow(window).IsComplete)
                .GroupBy(window => window.Pid).Select(group => new AudioChoice(AudioProcessIdentity.FromWindow(group.First()))));
            CmbGameAudio.ItemsSource = choices; CmbVoiceAudio.ItemsSource = choices;
            CmbGameAudio.SelectedItem = choices.FirstOrDefault(choice => Same(choice.Identity, oldGame)) ?? choices[0];
            CmbVoiceAudio.SelectedItem = choices.FirstOrDefault(choice => Same(choice.Identity, oldVoice)) ?? choices[0];
            TxtAudioSources.Text = "Choose running apps. Capture includes each selected process tree; app restarts require a fresh selection. Sound observation appears in Recording Health.";
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
            throw new ArgumentException("Choose valid output and microphone preferences under Audio Settings.");
        if (staged.AudioMode == "SystemFallback" && !output.IsAvailable)
            throw new ArgumentException("The selected audio output is disconnected. Refresh devices or choose Windows default.");
        if (ChkMicrophone.IsChecked == true && !input.IsAvailable)
            throw new ArgumentException("The selected microphone is disconnected. Refresh devices or choose Windows default.");
        staged.SystemAudioDeviceId = output.Id;
        staged.MicrophoneDeviceId = input.Id;
        AudioCapturePolicy.Validate(staged);
    }
}
