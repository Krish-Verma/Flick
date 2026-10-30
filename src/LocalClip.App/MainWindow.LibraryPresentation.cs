using System.Windows;

namespace LocalClip.App;

public partial class MainWindow
{
    private void ClearClipSearch_Click(object sender, RoutedEventArgs args)
    {
        // Clear the delayed target as well as the model so a pending search
        // cannot restore the old query after the user chooses Clear.
        TxtClipSearch.SetCurrentValue(System.Windows.Controls.TextBox.TextProperty, "");
        TxtClipSearch.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)?.UpdateSource();
        _vm.ClearLibrarySearch();
        TxtClipSearch.Focus();
    }

    private void ClearLibraryFilters_Click(object sender, RoutedEventArgs args)
    {
        _vm.ClearLibraryFilters();
        RadioAllClips.IsChecked = true;
    }

    private void ChooseCaptureSource_Click(object sender, RoutedEventArgs args)
    {
        CmbTargets.Focus();
        CmbTargets.IsDropDownOpen = true;
    }

    private bool NativePreviewAllowed => _vm.SelectedClip?.IsAvailableForUi == true &&
        !_vm.IsRecordingDiagnosticRunning && _vm.CaptureStatus != "Starting" && !_quitInProgress && !_updateInstallInProgress;

    private void UpdateSelectedAvailabilityUi(bool refreshPoster)
    {
        var clip = _vm.SelectedClip;
        bool available = clip?.IsAvailableForUi == true;
        bool canBuildPreview = available && !_vm.IsCaptureActive && !_vm.IsImporting && !_vm.IsExporting &&
            !_vm.IsBackgroundWorkActive && !_vm.IsRecordingDiagnosticRunning && _previewCancellation == null;
        bool canPlay = NativePreviewAllowed && _previewCancellation == null;
        BtnPlayPause.IsEnabled = BtnPlayPoster.IsEnabled = canPlay;
        BtnBuildPreview.IsEnabled = canBuildPreview;
        BtnPreviewMode.IsEnabled = NativePreviewAllowed; // Switching back also cancels a pending compatibility preview.
        BtnPlayPoster.ToolTip = canPlay ? "Play original video (Space)" :
            available ? "Preview is paused during other work" : "Original video is unavailable";
        if (!refreshPoster) return;

        if (!available)
        {
            if (VideoPlayer.Source != null) SuspendPlayback(clearSource: true);
            PlayerPoster.Visibility = PlayerPosterHint.Visibility = Visibility.Collapsed;
            PreviewFallbackPanel.Visibility = Visibility.Visible;
            bool checking = clip != null && !clip.HasKnownAvailability;
            TxtPreviewIssue.Text = clip == null ? "Choose a clip" : checking ? "Checking original…" : "Original video is offline";
            TxtPreviewFallbackHint.Text = clip == null ? "Save a replay or import a folder." :
                checking ? "You can keep browsing." : "Reconnect its folder to preview it.";
            BtnBuildPreview.Visibility = Visibility.Collapsed;
        }
        else if (VideoPlayer.Source == null)
        {
            PreviewFallbackPanel.Visibility = Visibility.Collapsed;
            BtnBuildPreview.Visibility = Visibility.Visible;
            PlayerPoster.Visibility = Visibility.Visible;
            PlayerPosterHint.Visibility = Visibility.Visible;
        }
        // Availability does not close Share/Trim/Audio or start a decoder.
    }
}
