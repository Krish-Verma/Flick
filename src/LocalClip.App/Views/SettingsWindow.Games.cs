using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using LocalClip.App.Services;
using LocalClip.Core.GameDetection;
using LocalClip.Core.Models;

namespace LocalClip.App.Views;

public partial class SettingsWindow
{
    private readonly ObservableCollection<ApprovedGameProfile> _stagedGameProfiles = [];
    private readonly WindowsGameWindowEnumerator _gameWindowEnumerator = new();
    private CancellationTokenSource? _gameApprovalCancellation;
    private Guid? _editingGameProfileId;
    private bool _loadingGames;
    private sealed record RunningGameChoice(GameWindowObservation Window)
    {
        public override string ToString() => Window.Title + " · " + Path.GetFileName(Window.Process.ExecutablePath);
    }

    private void PopulateGameProfiles(AppSettings settings)
    {
        _loadingGames = true;
        try
        {
            ChkGameDetectionEnabled.IsChecked = settings.GameDetectionEnabled;
            _editingGameProfileId = null;
            _stagedGameProfiles.Clear();
            foreach (var game in settings.ApprovedGameProfiles) _stagedGameProfiles.Add(game with { });
            LstApprovedGames.ItemsSource = _stagedGameProfiles;
            GameProfileEditor.Visibility = Visibility.Collapsed;
            LstApprovedGames.SelectedIndex = -1;
        }
        finally { _loadingGames = false; }
    }

    private async void RefreshRunningGames_Click(object sender, RoutedEventArgs e)
    {
        _gameApprovalCancellation?.Cancel();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        _gameApprovalCancellation = cancellation;
        BtnRefreshRunningGames.IsEnabled = false;
        CmbRunningGames.ItemsSource = null;
        TxtGameMessage.Text = "Finding games…";
        try
        {
            var snapshot = await Task.Run(() => _gameWindowEnumerator.Scan(cancellation.Token), cancellation.Token);
            if (snapshot.Status != GameScanStatus.Complete) throw new InvalidOperationException();
            var choices = snapshot.Windows.Where(window => window.Eligible && window.Process.Pid != Environment.ProcessId &&
                !string.Equals(window.Process.ExecutablePath, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase))
                .OrderBy(window => window.Title, StringComparer.OrdinalIgnoreCase).Select(window => new RunningGameChoice(window)).ToArray();
            CmbRunningGames.ItemsSource = choices;
            CmbRunningGames.SelectedIndex = choices.Length > 0 ? 0 : -1;
            TxtGameMessage.Text = choices.Length == 0 ? "No games found. Open a game and refresh."
                : "Choose a game, then approve it.";
            TxtGameMessage.ToolTip = "Only eligible, unelevated game windows are listed. Approving a path stages it without starting recording.";
        }
        catch (OperationCanceledException) { TxtGameMessage.Text = "Window discovery cancelled. No game was approved."; }
        catch { TxtGameMessage.Text = "Running windows could not be verified. No game was approved."; }
        finally
        {
            if (ReferenceEquals(_gameApprovalCancellation, cancellation)) _gameApprovalCancellation = null;
            BtnRefreshRunningGames.IsEnabled = true;
        }
    }

    private async void ApproveRunningGame_Click(object sender, RoutedEventArgs e)
    {
        if (CmbRunningGames.SelectedItem is not RunningGameChoice selected || !TryStageGameEditor(out _)) return;
        if (_stagedGameProfiles.Count >= GameDetectionPolicy.MaximumProfiles) { TxtGameMessage.Text = "Keep at most 32 approved games."; return; }
        var current = await Task.Run(() => _gameWindowEnumerator.Revalidate(selected.Window));
        if (current?.Eligible != true) { TxtGameMessage.Text = "That window changed. Find running windows again before approving it."; return; }
        if (_stagedGameProfiles.Any(profile => string.Equals(profile.ExecutablePath, current.Process.ExecutablePath, StringComparison.OrdinalIgnoreCase)))
        { TxtGameMessage.Text = "That executable is already approved. Select its profile below to edit it."; return; }
        string name = current.Title.Length > 120 ? current.Title[..120] : current.Title;
        name = new string(name.Where(character => !char.IsControl(character)).ToArray()).Trim();
        if (name.Length == 0) name = Path.GetFileNameWithoutExtension(current.Process.ExecutablePath);
        var profile = new ApprovedGameProfile { Id = Guid.NewGuid(), Enabled = true, Name = name, ExecutablePath = current.Process.ExecutablePath };
        _stagedGameProfiles.Add(profile);
        LstApprovedGames.SelectedItem = profile;
        TxtGameMessage.Text = "Game added. Save to apply.";
    }

    private void ApprovedGame_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingGames) return;
        var requested = LstApprovedGames.SelectedItem as ApprovedGameProfile;
        if (!TryStageGameEditor(out string error))
        {
            _loadingGames = true;
            LstApprovedGames.SelectedItem = _stagedGameProfiles.FirstOrDefault(profile => profile.Id == _editingGameProfileId);
            _loadingGames = false;
            TxtGameMessage.Text = error;
            return;
        }
        ShowGameEditor(requested?.Id);
    }

    private void ShowGameEditor(Guid? id)
    {
        _loadingGames = true;
        try
        {
            var profile = _stagedGameProfiles.FirstOrDefault(game => game.Id == id);
            _editingGameProfileId = profile?.Id;
            GameProfileEditor.Visibility = profile == null ? Visibility.Collapsed : Visibility.Visible;
            if (profile == null) return;
            TxtGameName.Text = profile.Name; TxtGameExecutable.Text = profile.ExecutablePath;
            ChkGameProfileEnabled.IsChecked = profile.Enabled;
            CmbGameQuality.SelectedIndex = profile.QualityPreset switch { "Light" => 0, "Smooth" => 2, _ => 1 };
            TxtGameDuration.Text = profile.ReplayDurationSeconds.ToString();
            ChkGameMicrophone.IsChecked = profile.AllowMicrophone;
        }
        finally { _loadingGames = false; }
    }

    private bool TryStageGameEditor(out string error)
    {
        error = "";
        if (_loadingGames || _editingGameProfileId == null) return true;
        var previous = _stagedGameProfiles.FirstOrDefault(game => game.Id == _editingGameProfileId);
        if (previous == null) return true;
        if (!int.TryParse(TxtGameDuration.Text, out int duration)) { error = "Choose a game replay duration from 15 to 120 seconds."; return false; }
        var edited = previous with { Name = TxtGameName.Text.Trim(), Enabled = ChkGameProfileEnabled.IsChecked == true,
            QualityPreset = CmbGameQuality.SelectedIndex switch { 0 => "Light", 2 => "Smooth", _ => "Balanced" },
            ReplayDurationSeconds = duration, AllowMicrophone = ChkGameMicrophone.IsChecked == true };
        if (!GameDetectionPolicy.ValidateProfiles([edited], out error)) return false;
        int index = _stagedGameProfiles.IndexOf(previous);
        bool wasSelected = LstApprovedGames.SelectedItem is ApprovedGameProfile selected && selected.Id == previous.Id;
        _loadingGames = true;
        try
        {
            _stagedGameProfiles[index] = edited;
            if (wasSelected) LstApprovedGames.SelectedItem = edited;
        }
        finally { _loadingGames = false; }
        return true;
    }

    private void ApplyGameProfile_Click(object sender, RoutedEventArgs e)
    {
        TxtGameMessage.Text = TryStageGameEditor(out string error)
            ? "Profile updated. Save to apply." : error;
    }

    private void RemoveGameProfile_Click(object sender, RoutedEventArgs e)
    {
        var profile = _stagedGameProfiles.FirstOrDefault(game => game.Id == _editingGameProfileId);
        _editingGameProfileId = null;
        if (profile != null) _stagedGameProfiles.Remove(profile);
        ShowGameEditor(null);
        TxtGameMessage.Text = "Game removed. Save to apply.";
    }

    private bool ReviewGameSettingsBeforeSave()
    {
        if (!TryStageGameEditor(out string error) || !GameDetectionPolicy.ValidateProfiles(_stagedGameProfiles.ToArray(), out error) ||
            (ChkGameDetectionEnabled.IsChecked == true && !_stagedGameProfiles.Any(profile => profile.Enabled)))
        {
            ShowSaveValidation("Games", ChkGameDetectionEnabled, "Review your game settings.");
            TxtGameMessage.Text = string.IsNullOrWhiteSpace(error) ? "Approve and enable at least one game first." : error;
            return false;
        }
        bool enabled = ChkGameDetectionEnabled.IsChecked == true;
        bool changed = enabled != _settings.GameDetectionEnabled ||
            JsonSerializer.Serialize(_stagedGameProfiles) != JsonSerializer.Serialize(_settings.ApprovedGameProfiles);
        if (!changed || (!enabled && !_settings.GameDetectionEnabled)) return true;
        string summary = enabled
            ? "Automatic replay will record only these approved executable paths when exactly one eligible game window is open. " +
              "It may start while Flick is in the tray. Stop Replay pauses the current game session; it stays paused after Flick restarts. " +
              "Changing active game settings stops automatic replay and discards its unsaved buffer. Saved clips are retained.\n\n" +
              string.Join("\n\n", _stagedGameProfiles.Where(game => game.Enabled).Select(game =>
                  game.Name + "\n" + game.ExecutablePath + "\n" + game.QualityPreset + " · " + game.ReplayDurationSeconds +
                  "s · microphone " + (game.AllowMicrophone ? "only if Audio also enables it" : "off")))
            : "Disable automatic game replay? Active automatic recording will stop and its unsaved buffer will be discarded. Saved clips and manual replay preferences are retained.";
        bool approved = FlickDialog.Show(summary, enabled ? "Approve Automatic Game Replay" : "Disable Automatic Game Replay",
            MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
        if (!approved) TxtSaveStatus.Text = "Changes not saved.";
        return approved;
    }
}
