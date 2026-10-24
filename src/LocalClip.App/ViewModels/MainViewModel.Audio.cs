using LocalClip.Core.Audio;
using LocalClip.Core.Models;
using LocalClip.Core.Media;
namespace LocalClip.App.ViewModels;

public partial class MainViewModel
{
    private bool _isBuildingAudioPreview;
    private readonly AudioPreviewOperation _audioPreviewOperation = new();
    private int _audioMixRevision;
    public int AudioMixRevision => _audioMixRevision;
    public bool CanPresentAudioMixPreview => !_isPreparingToQuit && !IsCaptureActive && !IsExporting;
    public void SetBuildingAudioPreview(bool value) { _isBuildingAudioPreview = value; OnPropertyChanged(nameof(IsBackgroundWorkActive)); OnPropertyChanged(nameof(CanEditClip)); OnPropertyChanged(nameof(CanStartExport)); }
    public async Task<string> CreateAudioMixPreviewAsync(ClipMetadata clip, CancellationToken cancellationToken)
    {
        if (IsCaptureActive || IsExporting || IsBackgroundWorkActive || _isPreparingToQuit)
            throw new InvalidOperationException("Finish active work before previewing audio.");
        var tracks = clip.AudioTracks.Select(track => new AudioTrackInfo { TrackIndex = track.TrackIndex, StreamIndex = track.StreamIndex,
            Name = track.Name, Role = track.Role, IsMuted = track.IsMuted, Gain = track.Gain }).ToArray();
        string original = clip.OriginalFilePath; double start = clip.TrimStartSeconds; double? end = clip.TrimEndSeconds > 0 ? clip.TrimEndSeconds : null;
        SetBuildingAudioPreview(true); // Reserve before starting the worker.
        try {
            return await _audioPreviewOperation.StartAsync(token => _prober.BuildPlaybackPreviewAsync(original, token,
                forceTranscode: true, audioMix: tracks, trimStart: start, trimEnd: end), cancellationToken);
        }
        finally { SetBuildingAudioPreview(false); }
    }
    public Task<bool> CancelAudioPreviewAndWaitAsync() => _audioPreviewOperation.CancelAndWaitAsync();
    private string _audioCaptureSummary = "Audio routing is checked when replay starts.";
    public string AudioCaptureSummary => _audioCaptureSummary;
    public async Task<TargetList> GetAudioSourcesAsync()
    {
        if (IsCaptureActive || IsReplaySavePending || _refreshingTargets)
            throw new InvalidOperationException("Stop replay and finish saving before refreshing app audio sources.");
        return await _captureHost.GetTargetsAsync();
    }
    private void SetAudioCaptureStatus(CaptureAudioStatus? audio)
    {
        _audioCaptureSummary = audio?.Summary ?? "Audio routing not reported by this helper.";
        OnPropertyChanged(nameof(AudioCaptureSummary));
    }
}
