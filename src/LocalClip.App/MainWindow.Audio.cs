using System;
using System.Linq;
using System.Threading;
using System.Windows;
using LocalClip.Core.Media;
using LocalClip.Core.Models;
using LocalClip.Core.Rules;
namespace LocalClip.App;

public partial class MainWindow
{
    private async void BuildAudioMixPreview_Click(object sender, RoutedEventArgs e)
    {
        var clip = _vm.SelectedClip;
        if (clip?.IsAvailableForUi != true || _vm.IsCaptureActive || _vm.IsExporting || _vm.IsBackgroundWorkActive)
        {
            _vm.StatusDetails = "Finish recording and background work before previewing the audio mix."; return;
        }
        CancelPendingPreview();
        var cancellation = new CancellationTokenSource();
        _previewCancellation = cancellation;
        string fingerprint = ExportFingerprint.Compute(clip.OriginalFilePath, clip.TrimStartSeconds, clip.TrimEndSeconds, clip.AudioTracks, _vm.TargetSizeMb);
        BtnAudioMixPreview.IsEnabled = false;
        _vm.StatusDetails = "Creating a local preview of the selected audio mix…";
        try
        {
            string path = await _vm.CreateAudioMixPreviewAsync(clip, cancellation.Token);
            if (cancellation.IsCancellationRequested || !_vm.CanPresentAudioMixPreview || _vm.SelectedClip?.Id != clip.Id || fingerprint !=
                ExportFingerprint.Compute(clip.OriginalFilePath, clip.TrimStartSeconds, clip.TrimEndSeconds, clip.AudioTracks, _vm.TargetSizeMb)) return;
            _previewWasReencoded = true;
            SetPreviewSource(path, PlayerView.AudioMix, true);
            _vm.StatusDetails = "Audio mix preview · trim and levels applied · up to 3 minutes · original unchanged";
        }
        catch (OperationCanceledException) { }
        catch (Exception) { if (_vm.SelectedClip?.Id == clip.Id) _vm.StatusDetails = "The audio mix preview could not be created. Check the original and try again."; }
        finally
        {
            if (ReferenceEquals(_previewCancellation, cancellation)) _previewCancellation = null;
            cancellation.Dispose();
            BtnAudioMixPreview.IsEnabled = true;
        }
    }
}
