using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using LocalClip.App.Services;
using LocalClip.App.ViewModels;
using LocalClip.App.Views.Components;
using LocalClip.Core.Models;
using LocalClip.Core.Rules;

namespace LocalClip.App;

public partial class MainWindow
{
    private bool ConfirmDiscardOrganization() => !_vm.HasUnsavedOrganization || FlickDialog.Show(
        "Clip details have not been saved. Discard those changes?", "Unsaved clip details",
        MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;
    private void SaveOrganization_Click(object sender, RoutedEventArgs e) => _vm.SaveOrganization();
    private void ToggleFavorite_Click(object sender, RoutedEventArgs e) => _vm.ToggleFavorite();
    private bool _selectingRecentExport;
    private void RecentExport_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_selectingRecentExport || sender is not ComboBox { SelectedItem: RecentClipExport recent } selector) return;
        _selectingRecentExport = true;
        try
        {
            if (!_vm.IsExporting && _vm.SelectRecentExport(recent.ClipId))
            {
                UpdateSharePresetSelection();
                _vm.ActivateSharePresentation();
            }
        }
        finally { selector.SelectedIndex = -1; _selectingRecentExport = false; }
    }
    private double OriginalPlayhead => Math.Clamp(PlayerSlider.Value + (_playerView == PlayerView.Exported
        ? _vm.SelectedClip?.TrimStartSeconds ?? 0 : 0), 0, _vm.SelectedClip?.DurationSeconds ?? 0);
    private void SeekOriginal(double seconds)
    {
        if (_vm.SelectedClip is not { } clip || !clip.IsAvailableForUi || _vm.IsCaptureActive) return;
        seconds = Math.Clamp(seconds, 0, clip.DurationSeconds);
        if (_playerView == PlayerView.Exported || VideoPlayer.Source == null) ShowOriginalPreview(startPlaying: false);
        PlayerSlider.Maximum = Math.Max(.25, clip.DurationSeconds); PlayerSlider.Value = seconds;
        VideoPlayer.Position = TimeSpan.FromSeconds(seconds); TrimTimeline.SetPlayhead(seconds);
    }
    private void RefreshTrimFromModel()
    {
        var clip = _vm.SelectedClip;
        TrimTimeline.SetRange(clip?.DurationSeconds ?? 0, clip?.TrimStartSeconds ?? 0, clip?.TrimEndSeconds ?? 0);
        TrimTimeline.SetPlayhead(OriginalPlayhead);
    }
    private void SetTrimIn_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedClip is not { } clip || clip.DurationSeconds < TrimTimelineMath.MinSelectionSeconds) return;
        double end = clip.TrimEndSeconds > 0 ? clip.TrimEndSeconds : clip.DurationSeconds;
        if (_vm.ApplyTrim(TrimTimelineMath.MoveStart(OriginalPlayhead, end, clip.DurationSeconds), end)) RefreshTrimFromModel();
    }
    private void SetTrimOut_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedClip is not { } clip || clip.DurationSeconds < TrimTimelineMath.MinSelectionSeconds) return;
        if (_vm.ApplyTrim(clip.TrimStartSeconds, TrimTimelineMath.MoveEnd(OriginalPlayhead, clip.TrimStartSeconds, clip.DurationSeconds))) RefreshTrimFromModel();
    }
    private void UndoTrim_Click(object sender, RoutedEventArgs e) { if (_vm.UndoTrim()) RefreshTrimFromModel(); }
    private void AddMarker_Click(object sender, RoutedEventArgs e) => _vm.AddMarker(OriginalPlayhead);
    private void SeekMarker_Click(object sender, RoutedEventArgs e)
    { if (sender is Button { Tag: ClipMarker marker } && _vm.SelectedClip?.Markers.Exists(existing => ReferenceEquals(existing, marker)) == true) SeekOriginal(marker.PositionSeconds); }
    private void RemoveMarker_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem item && item.Parent is ContextMenu { PlacementTarget: Button { Tag: ClipMarker marker } }) _vm.RemoveMarker(marker);
    }
    private async void LoadFilmstrip_Click(object sender, RoutedEventArgs e) => await _vm.LoadFilmstripAsync();
    private void SeekFrame_Click(object sender, RoutedEventArgs e)
    { if (sender is Button { Tag: TimelineFrame frame } && frame.ClipId == _vm.SelectedClip?.Id) SeekOriginal(frame.Seconds); }
    private void TrimTimeline_RangeChanging(object? sender, EventArgs e) => _vm.ReportUnsavedTrim();
    private void TrimTimeline_RangeReverted(object? sender, EventArgs e) => _vm.ReportRevertedTrim();
    private void TrimTimeline_SeekRequested(object? sender, TrimSeekEventArgs e) => SeekOriginal(e.Seconds);
    private bool HandleEditorShortcut(KeyEventArgs e)
    {
        if (_vm.SelectedClip == null || !TrimPanel.IsVisible || ClipLibraryPanel.IsKeyboardFocusWithin || Keyboard.FocusedElement is TextBoxBase or PasswordBox or ComboBox or Slider or Thumb) return false;
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Z) { UndoTrim_Click(this, new()); return true; }
        if (Keyboard.Modifiers is not (ModifierKeys.None or ModifierKeys.Control)) return false;
        switch (e.Key)
        {
            case Key.I when Keyboard.Modifiers == ModifierKeys.None: SetTrimIn_Click(this, new()); return true;
            case Key.O when Keyboard.Modifiers == ModifierKeys.None: SetTrimOut_Click(this, new()); return true;
            case Key.M when Keyboard.Modifiers == ModifierKeys.None: AddMarker_Click(this, new()); return true;
            case Key.Left: SeekOriginal(OriginalPlayhead - (Keyboard.Modifiers == ModifierKeys.Control ? 5 : 1)); return true;
            case Key.Right: SeekOriginal(OriginalPlayhead + (Keyboard.Modifiers == ModifierKeys.Control ? 5 : 1)); return true;
            case Key.Home: SeekOriginal(0); return true;
            case Key.End: SeekOriginal(_vm.SelectedClip.DurationSeconds); return true;
            default: return false;
        }
    }
}
