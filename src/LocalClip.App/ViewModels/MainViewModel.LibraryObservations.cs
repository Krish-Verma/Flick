using System.IO;
using System.Text.Json;
using System.Windows.Threading;
using LocalClip.Core.Models;
using LocalClip.Core.Rules;
using LocalClip.Core.Storage;

namespace LocalClip.App.ViewModels;

public partial class MainViewModel
{
    private readonly LibraryObservationReader _libraryReader = new();
    private readonly Dispatcher _libraryDispatcher = Dispatcher.CurrentDispatcher;
    private readonly Func<string, bool> _libraryPathExists = File.Exists;
    private CancellationTokenSource? _libraryAvailabilityCancellation, _selectedLibraryCancellation, _recentLibraryCancellation;
    private Task _libraryAvailabilityTask = Task.CompletedTask, _selectedLibraryTask = Task.CompletedTask, _recentLibraryTask = Task.CompletedTask;
    private int _libraryAvailabilityGeneration, _selectedLibraryGeneration, _recentLibraryGeneration;
    private bool _libraryObservationsDisposed;
    private int _selectedClipAvailabilityRevision;
    public int SelectedClipAvailabilityRevision => _selectedClipAvailabilityRevision;

    private sealed record AvailabilityRead(ClipMetadata Clip, string Path, bool Available);
    private sealed record ShareRead(ClipMetadata Clip, ClipMetadata Snapshot, string Signature);
    private sealed record SelectedRead(bool Available, bool ThumbnailExists, bool Ready, string ReadyPath);

    /// <summary>Fixture/UI settlement only: caller on STA must keep pumping its dispatcher. No new IO is started.</summary>
    public async Task WhenLibraryPresentationSettledAsync(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            var tasks = new[] { _libraryAvailabilityTask, _selectedLibraryTask, _recentLibraryTask };
            try { await Task.WhenAll(tasks).WaitAsync(cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
            if (ReferenceEquals(tasks[0], _libraryAvailabilityTask) && ReferenceEquals(tasks[1], _selectedLibraryTask) &&
                ReferenceEquals(tasks[2], _recentLibraryTask)) return;
        }
    }

    private static ShareRead SnapshotShareRead(ClipMetadata clip)
    {
        string metadata = JsonSerializer.Serialize(clip); // Availability/computed IO properties are JsonIgnore.
        return new(clip, JsonSerializer.Deserialize<ClipMetadata>(metadata)!, ExportReadinessIdentity.Capture(clip));
    }
    private static bool Matches(ShareRead read) => read.Clip.OriginalFilePath == read.Snapshot.OriginalFilePath &&
        ExportReadinessIdentity.Capture(read.Clip) == read.Signature;

    private Task OnLibraryDispatcherAsync(Action apply)
    {
        if (_libraryDispatcher.HasShutdownStarted || _libraryDispatcher.HasShutdownFinished) return Task.CompletedTask;
        if (_libraryDispatcher.CheckAccess()) { apply(); return Task.CompletedTask; }
        return _libraryDispatcher.InvokeAsync(apply).Task;
    }

    private void StartLibraryAvailabilityRefresh()
    {
        _libraryAvailabilityCancellation?.Cancel();
        if (_libraryObservationsDisposed) return;
        var cancellation = new CancellationTokenSource();
        _libraryAvailabilityCancellation = cancellation;
        int generation = ++_libraryAvailabilityGeneration;
        var items = _allClips.OrderByDescending(clip => ReferenceEquals(clip, SelectedClip))
            .Select(clip => (Clip: clip, Path: clip.OriginalFilePath)).ToArray();
        _libraryAvailabilityTask = ReadLibraryAvailabilityAsync(items, generation, cancellation);
    }

    private async Task ReadLibraryAvailabilityAsync((ClipMetadata Clip, string Path)[] items, int generation, CancellationTokenSource cancellation)
    {
        try
        {
            for (int offset = 0; offset < items.Length; offset += 32)
            {
                var batch = items.Skip(offset).Take(32).ToArray();
                var observed = await _libraryReader.ReadAsync(LibraryObservationPriority.Availability, token =>
                {
                    var results = new List<AvailabilityRead>(batch.Length);
                    foreach (var item in batch)
                    {
                        token.ThrowIfCancellationRequested();
                        results.Add(new(item.Clip, item.Path, _libraryPathExists(item.Path)));
                    }
                    return results;
                }, cancellation.Token).ConfigureAwait(false);
                await OnLibraryDispatcherAsync(() =>
                {
                    if (_libraryObservationsDisposed || cancellation.IsCancellationRequested || generation != _libraryAvailabilityGeneration) return;
                    foreach (var item in observed) ApplyLibraryAvailability(item);
                }).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            // A read failure is not evidence that every original is offline.
            // Keep unknown/previous observations until a deliberate reload retries.
        }
        finally
        {
            await OnLibraryDispatcherAsync(() => { if (ReferenceEquals(_libraryAvailabilityCancellation, cancellation)) _libraryAvailabilityCancellation = null; }).ConfigureAwait(false);
            cancellation.Dispose();
        }
    }

    private void ApplyLibraryAvailability(AvailabilityRead read)
    {
        if (!read.Clip.ApplyAvailabilityObservation(read.Path, read.Available) || !ReferenceEquals(read.Clip, SelectedClip)) return;
        PublishSelectedAvailability();
        if (!read.Available && !IsExporting) ClearReadinessPresentation("Original video offline · reconnect its folder");
    }

    private void PublishSelectedAvailability()
    {
        _selectedClipAvailabilityRevision++;
        OnPropertyChanged(nameof(SelectedClipAvailabilityRevision));
        OnPropertyChanged(nameof(CanEditClip));
        OnPropertyChanged(nameof(CanStartExport));
    }

    private void ClearReadinessPresentation(string message)
    {
        IsExportReady = false;
        ReadyExportFilePath = "";
        ExportStatusMessage = message;
    }

    private void CancelSelectedLibraryRead()
    {
        _selectedLibraryGeneration++;
        _selectedLibraryCancellation?.Cancel();
    }

    private void StartSelectedLibraryRead()
    {
        CancelSelectedLibraryRead();
        if (_libraryObservationsDisposed || SelectedClip is not { } clip) return;
        var read = SnapshotShareRead(clip);
        int target = TargetSizeMb;
        string exports = _settings.ExportsFolder;
        int generation = _selectedLibraryGeneration;
        bool allowThumbnail = CanGenerateSelectedThumbnail;
        var cancellation = new CancellationTokenSource();
        _selectedLibraryCancellation = cancellation;
        _selectedLibraryTask = ReadSelectedLibraryAsync(read, target, exports, generation, allowThumbnail, cancellation);
    }

    private async Task ReadSelectedLibraryAsync(ShareRead read, int target, string exports, int generation, bool allowThumbnail, CancellationTokenSource cancellation)
    {
        try
        {
            var result = await _libraryReader.ReadAsync(LibraryObservationPriority.SelectedClip, token =>
            {
                bool available = _libraryPathExists(read.Snapshot.OriginalFilePath);
                token.ThrowIfCancellationRequested();
                bool thumbnail = !string.IsNullOrWhiteSpace(read.Snapshot.ThumbnailPath) && _libraryPathExists(read.Snapshot.ThumbnailPath);
                token.ThrowIfCancellationRequested();
                string path = "";
                bool ready = available && ReadyShareFile.TryGet(read.Snapshot, target, out path, exports);
                return new SelectedRead(available, thumbnail, ready, path);
            }, cancellation.Token).ConfigureAwait(false);
            await OnLibraryDispatcherAsync(() =>
            {
                if (_libraryObservationsDisposed || cancellation.IsCancellationRequested || generation != _selectedLibraryGeneration ||
                    !ReferenceEquals(SelectedClip, read.Clip) || TargetSizeMb != target || _settings.ExportsFolder != exports || !Matches(read)) return;
                ApplyLibraryAvailability(new(read.Clip, read.Snapshot.OriginalFilePath, result.Available));
                if (IsExporting) return;
                if (!result.Available) { ClearReadinessPresentation("Original video offline · reconnect its folder"); return; }
                IsExportReady = result.Ready;
                ReadyExportFilePath = result.Ready ? result.ReadyPath : "";
                ExportStatusMessage = result.Ready ? $"Ready to share · {read.Snapshot.ExportedFileSizeBytes / 1_000_000d:F2} MB"
                    : "Export to prepare an MP4.";
                if (read.Clip.ThumbnailPath == read.Snapshot.ThumbnailPath && allowThumbnail &&
                    !result.ThumbnailExists && CanGenerateSelectedThumbnail) _ = EnsureSelectedThumbnailAsync(read.Clip);
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (Exception) { /* Unknown stays unknown; explicit commands retain their fresh last-mile guards. */ }
        finally
        {
            await OnLibraryDispatcherAsync(() => { if (ReferenceEquals(_selectedLibraryCancellation, cancellation)) _selectedLibraryCancellation = null; }).ConfigureAwait(false);
            cancellation.Dispose();
        }
    }

    private void StartRecentExportsRead()
    {
        _recentLibraryCancellation?.Cancel();
        RecentExports.Clear(); _recentExportIds.Clear();
        if (_libraryObservationsDisposed) return;
        var reads = _allClips.Where(clip => clip.LastExportedAtUtc != null && clip.IsExportReady)
            .OrderByDescending(clip => clip.LastExportedAtUtc).Take(40).Select(SnapshotShareRead).ToArray();
        int generation = ++_recentLibraryGeneration;
        var cancellation = new CancellationTokenSource();
        _recentLibraryCancellation = cancellation;
        _recentLibraryTask = ReadRecentExportsAsync(reads, _settings.ExportsFolder, generation, cancellation);
    }

    private async Task ReadRecentExportsAsync(ShareRead[] reads, string exports, int generation, CancellationTokenSource cancellation)
    {
        try
        {
            for (int offset = 0; offset < reads.Length && !cancellation.IsCancellationRequested; offset += 4)
            {
                var batch = reads.Skip(offset).Take(4).ToArray();
                var valid = await _libraryReader.ReadAsync(LibraryObservationPriority.RecentExports, token =>
                {
                    var results = new List<(ShareRead Read, string Path)>();
                    foreach (var read in batch)
                    {
                        token.ThrowIfCancellationRequested();
                        if (ReadyShareFile.TryGet(read.Snapshot, read.Snapshot.LastExportTargetMb, out var path, exports)) results.Add((read, path));
                    }
                    return results;
                }, cancellation.Token).ConfigureAwait(false);
                await OnLibraryDispatcherAsync(() =>
                {
                    if (_libraryObservationsDisposed || cancellation.IsCancellationRequested || generation != _recentLibraryGeneration || _settings.ExportsFolder != exports) return;
                    foreach (var item in valid)
                    {
                        if (RecentExports.Count == 20) break;
                        var clip = item.Read.Clip;
                        if (!_allClips.Contains(clip) || !Matches(item.Read)) continue;
                        RecentExports.Add(new(clip.Id, clip.DisplayTitle, item.Path, clip.ExportedFileSizeBytes,
                            clip.LastExportedAtUtc!.Value, clip.ExportAspectPreset, clip.LastExportTargetMb));
                        _recentExportIds.Add(clip.Id);
                    }
                    if (LibrarySort == "Recently Exported") ApplyFilter();
                }).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception) { /* Failed observations never manufacture a Ready history entry. */ }
        finally
        {
            await OnLibraryDispatcherAsync(() => { if (ReferenceEquals(_recentLibraryCancellation, cancellation)) _recentLibraryCancellation = null; }).ConfigureAwait(false);
            cancellation.Dispose();
        }
    }

    private void StopLibraryObservations(bool disposing = false)
    {
        _libraryAvailabilityGeneration++; _selectedLibraryGeneration++; _recentLibraryGeneration++;
        _libraryAvailabilityCancellation?.Cancel(); _selectedLibraryCancellation?.Cancel(); _recentLibraryCancellation?.Cancel();
        if (disposing) { _libraryObservationsDisposed = true; _libraryReader.Dispose(); }
    }

    private void ResumeLibraryObservationsAfterRefusedQuit()
    {
        if (_libraryObservationsDisposed) return;
        StartSelectedLibraryRead();
        StartRecentExportsRead();
        StartLibraryAvailabilityRefresh();
    }
}
