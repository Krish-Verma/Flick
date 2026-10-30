using System.ComponentModel;
using System.Globalization;
using System.Windows.Media;
using LocalClip.App.Services;
using LocalClip.Core.Models;

namespace LocalClip.App.ViewModels;

public partial class MainViewModel
{
    private bool _sharePresentationActive;
    private bool _shareCancellationRequested;
    private bool _shareDisposed;
    private ClipMetadata? _shareObservedClip;
    private CancellationTokenSource? _sharePreviewCancellation;
    private readonly SemaphoreSlim _sharePreviewGate = new(1, 1);
    private ImageSource? _sharePreview;

    public ImageSource? SharePreview => _sharePreview;
    public bool ShareHighContrast => System.Windows.SystemParameters.HighContrast;
    internal void NotifyShareAccessibility() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShareHighContrast)));
    public bool ShareHasPreview => _sharePreview != null;
    public bool ShareCanCopy => SelectedClip != null && IsExportReady && !IsExporting;
    public bool ShareCanCancel => IsExporting && !_shareCancellationRequested;
    public string ShareClipTitle => SelectedClip?.DisplayTitle ?? "Choose a clip to share";
    public string ShareClipDetails => SelectedClip == null ? "Your originals stay unchanged."
        : FormatShareDuration(SelectedClip.SelectedDurationSeconds) +
          (IsExportReady ? $" · MP4 · {SelectedClip.ExportedFileSizeBytes / 1_000_000d:0.00} MB" : " · Original preview");
    public string ShareTileStateText => SelectedClip == null ? "No clip selected"
        : IsExporting ? (_shareCancellationRequested ? "Finishing cancellation…" : ShareExportStageText)
        : IsExportReady ? "MP4 ready"
        : ExportStatusMessage.StartsWith("Checking ", StringComparison.OrdinalIgnoreCase)
            ? (SelectedClip.HasKnownAvailability && SelectedClip.IsAvailableForUi ? "Checking MP4…" : "Checking original…")
        : ExportStatusMessage.StartsWith("Original Video Offline", StringComparison.OrdinalIgnoreCase) ? "Original offline"
        : ExportStatusMessage.StartsWith("Export Failed", StringComparison.OrdinalIgnoreCase) ? "Export failed"
        : ExportStatusMessage.StartsWith("Export Cancelled", StringComparison.OrdinalIgnoreCase) ? "Export cancelled"
        : !string.IsNullOrEmpty(SelectedClip.ExportedFilePath) ? "A new export is needed" : "Prepare your MP4";
    public string ShareTileHint => SelectedClip == null ? "Select a clip from Your clips."
        : IsExporting ? (_shareCancellationRequested ? "Cancelling export…" : "Your original stays unchanged.")
        : IsExportReady ? "Drag this MP4 into Discord · Ctrl+C to copy."
        : ShareTileStateText.StartsWith("Checking ", StringComparison.Ordinal) ? "You can keep browsing."
        : ShareTileStateText == "Original offline" ? "Reconnect its folder before exporting."
        : ShareTileStateText == "Export failed" ? "Try export again, or open Details."
        : ShareTileStateText == "Export cancelled" ? "Export again when you're ready."
        : "Choose a size, then export.";
    // Project actual exporter stages; encoding percentages are not a promise
    // about the remaining validation/finalization work.
    private string ShareExportStageText => ExportStatusMessage switch
    {
        string text when text.StartsWith("Calculating bitrate budget", StringComparison.OrdinalIgnoreCase) => "Sizing MP4…",
        string text when text.StartsWith("Encoding MP4", StringComparison.OrdinalIgnoreCase) => "Encoding MP4…",
        string text when text.StartsWith("Validating exported streams", StringComparison.OrdinalIgnoreCase) => "Checking MP4 streams…",
        string text when text.StartsWith("Running full decode", StringComparison.OrdinalIgnoreCase) => "Checking playback…",
        string text when text.StartsWith("Finalizing share file", StringComparison.OrdinalIgnoreCase) => "Finishing MP4…",
        _ => "Preparing MP4…"
    };
    public string ShareFormatHint => (ExportAspectPreset switch
    {
        "Vertical9x16" => "Fit 9:16 · no crop · ",
        "Square1x1" => "Fit 1:1 · no crop · ",
        _ => "Original shape · "
    }) + $"up to {TargetSizeMb.ToString(CultureInfo.CurrentCulture)} MB";
    public string ShareStatusSummary
    {
        get
        {
            string status = ExportStatusMessage;
            if (IsExporting) return ""; // The tile owns the current stage; Details retains full diagnostics.
            if (status.StartsWith("Checking ", StringComparison.OrdinalIgnoreCase) || status == "Export to prepare an MP4.")
                return ""; // Readiness and the next action already appear on the tile.
            if (IsExportReady && status.StartsWith("Ready to share", StringComparison.OrdinalIgnoreCase))
                return ""; // The tile already shows readiness and actual size.
            if (status.Length <= 72) return status;
            if (status.StartsWith("Export Failed", StringComparison.OrdinalIgnoreCase))
                return "Export failed. Open Details.";
            if (status.StartsWith("Post failed", StringComparison.OrdinalIgnoreCase))
                return "Posting failed. Open Details.";
            return "Open Details for more.";
        }
    }

    public string ShareSizeHint => $"Maximum {TargetSizeMb.ToString(CultureInfo.CurrentCulture)} MB · destination limits may vary";

    // Revalidate the current per-file cap without replacing it with the
    // global preference (recent exports deliberately restore a transient cap).
    public void RecheckShareReadiness() => OnSelectedClipChanged();

    public void ActivateSharePresentation()
    {
        if (_shareDisposed) return;
        _sharePresentationActive = true;
        OnSelectedClipChanged(); // Recheck a cached export on opening, never encode or post.
        ObserveShareClip();
        NotifySharePresentation();
    }

    public void DeactivateSharePresentation()
    {
        _sharePresentationActive = false;
        _sharePreviewCancellation?.Cancel();
        if (_shareObservedClip != null) _shareObservedClip.PropertyChanged -= OnShareClipPropertyChanged;
        _shareObservedClip = null;
        _sharePreview = null;
        NotifySharePresentation();
    }

    public void CancelShareExport()
    {
        if (!ShareCanCancel) return;
        _shareCancellationRequested = true;
        CancelExport();
        ExportStatusMessage = "Cancelling export…";
        NotifySharePresentation();
    }

    private void ObserveShareClip()
    {
        if (!_sharePresentationActive || _shareDisposed) return;
        if (_shareObservedClip != null) _shareObservedClip.PropertyChanged -= OnShareClipPropertyChanged;
        _shareObservedClip = SelectedClip;
        if (_shareObservedClip != null) _shareObservedClip.PropertyChanged += OnShareClipPropertyChanged;
        _ = RefreshSharePreviewAsync(_shareObservedClip);
    }

    private void OnShareClipPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(ClipMetadata.ThumbnailPath) && sender is ClipMetadata clip)
            System.Windows.Application.Current?.Dispatcher.InvokeAsync(() => NotifyShareThumbnailAvailable(clip));
    }

    /// <summary>
    /// Call after successful thumbnail regeneration, even when its path did not
    /// change. Queued work must still belong to the currently visible Share clip.
    /// </summary>
    public void NotifyShareThumbnailAvailable(ClipMetadata clip)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished) return;
        if (!dispatcher.CheckAccess())
        {
            dispatcher.InvokeAsync(() => NotifyShareThumbnailAvailable(clip));
            return;
        }
        if (!CanRefreshSharePreview(clip)) return;
        _ = RefreshSharePreviewAsync(clip);
    }

    private bool CanRefreshSharePreview(ClipMetadata? clip) =>
        !_shareDisposed && _sharePresentationActive && ReferenceEquals(clip, SelectedClip) &&
        ReferenceEquals(clip, _shareObservedClip);

    private async Task RefreshSharePreviewAsync(ClipMetadata? clip)
    {
        // Reject stale notifications before cancelling or clearing the current
        // clip's work. The same fence also prevents reopening a closed drawer.
        if (!CanRefreshSharePreview(clip)) return;
        var cancellation = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _sharePreviewCancellation, cancellation);
        previous?.Cancel();
        _sharePreview = null;
        NotifySharePresentation();
        bool acquired = false;
        try
        {
            await _sharePreviewGate.WaitAsync(cancellation.Token);
            acquired = true;
            string path = clip?.ThumbnailPath ?? "";
            var preview = await Task.Run(() => SharePreviewLoader.LoadAsync(path, cancellation.Token), cancellation.Token);
            if (!cancellation.IsCancellationRequested && CanRefreshSharePreview(clip)) _sharePreview = preview;
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (acquired) _sharePreviewGate.Release();
            if (ReferenceEquals(_sharePreviewCancellation, cancellation)) _sharePreviewCancellation = null;
            cancellation.Dispose();
            if (!_shareDisposed) NotifySharePresentation();
        }
    }

    private void OnShareDependencyChanged(string? name)
    {
        if (name is nameof(CaptureStatus) or nameof(IsBackgroundWorkActive) or nameof(IsExporting))
            if (IsCaptureActive || IsExporting) _sharePreviewCancellation?.Cancel();
        if (name == nameof(SelectedClip)) ObserveShareClip();
        if (name == nameof(IsExporting) && !IsExporting) _shareCancellationRequested = false;
        if (name is nameof(SelectedClip) or nameof(IsExporting) or nameof(IsExportReady)
            or nameof(ExportStatusMessage) or nameof(ReadyExportFilePath) or nameof(TargetSizeMb) or nameof(ExportAspectPreset))
            NotifySharePresentation();
    }

    private void NotifySharePresentation()
    {
        foreach (string name in new[] { nameof(SharePreview), nameof(ShareHasPreview), nameof(ShareCanCopy),
                     nameof(ShareCanCancel), nameof(ShareClipTitle), nameof(ShareClipDetails), nameof(ShareTileStateText),
                     nameof(ShareTileHint), nameof(ShareSizeHint), nameof(ShareFormatHint), nameof(ShareStatusSummary) })
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    private void DisposeSharePresentation()
    {
        _shareDisposed = true;
        DeactivateSharePresentation();
    }

    private static string FormatShareDuration(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0) return "0:00";
        var duration = TimeSpan.FromSeconds(Math.Min(seconds, TimeSpan.MaxValue.TotalSeconds / 2));
        return duration.TotalHours >= 1 ? duration.ToString(@"h\:mm\:ss") : duration.ToString(@"m\:ss");
    }
}
