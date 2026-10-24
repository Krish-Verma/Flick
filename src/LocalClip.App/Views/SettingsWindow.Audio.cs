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
    private sealed record AudioChoice(AudioProcessIdentity? Identity)
    {
        public string Label => Identity?.DisplayLabel ?? "None";
    }
    private void AudioMode_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (AppAudioSourcesPanel != null) AppAudioSourcesPanel.Visibility = CmbAudioMode.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
    }
    private void PopulateAudio(AppSettings settings)
    {
        CmbAudioMode.SelectedIndex = AudioCapturePolicy.IsSeparated(settings) ? 1 : 0;
        CmbGameAudio.ItemsSource = new[] { new AudioChoice(null), new AudioChoice(settings.GameAudioSource) }.Distinct().ToArray();
        CmbVoiceAudio.ItemsSource = new[] { new AudioChoice(null), new AudioChoice(settings.VoiceAudioSource) }.Distinct().ToArray();
        CmbGameAudio.SelectedIndex = settings.GameAudioSource == null ? 0 : 1;
        CmbVoiceAudio.SelectedIndex = settings.VoiceAudioSource == null ? 0 : 1;
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
        ChkMicrophone.IsChecked == true != _settings.EnableMicrophone;
    private void StageAudio(AppSettings staged)
    {
        staged.AudioMode = CmbAudioMode.SelectedIndex == 1 ? "Separated" : "SystemFallback";
        staged.GameAudioSource = (CmbGameAudio.SelectedItem as AudioChoice)?.Identity;
        staged.VoiceAudioSource = (CmbVoiceAudio.SelectedItem as AudioChoice)?.Identity;
        AudioCapturePolicy.Validate(staged);
    }
}
