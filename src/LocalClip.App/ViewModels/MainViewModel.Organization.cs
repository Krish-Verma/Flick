using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using LocalClip.Core.Models;
using LocalClip.Core.Rules;
using LocalClip.App.Services;
using LocalClip.Core.Media;

namespace LocalClip.App.ViewModels;

public sealed record TimelineFrame(string ClipId, BitmapSource Image, double Seconds, string Label);

public partial class MainViewModel
{
    private bool _favoritesOnly;
    private string _gameFilter = "All Games", _librarySort = "Newest";
    private string _libraryPeriod = "Any time";
    private readonly HashSet<string> _recentExportIds = new(StringComparer.Ordinal);
    private bool _applyingLibraryFilter;
    private string _tagsDraft = "", _gameDraft = "", _editorSaveState = "Edits saved";
    private readonly List<(double Start, double End)> _trimUndo = [];
    private bool _undoingTrim;
    public Func<bool>? ConfirmDiscardEditorChanges { get; set; }
    public ObservableCollection<string> GameGroups { get; } = ["All Games"];
    public IReadOnlyList<string> LibrarySortChoices { get; } = new[] { "Newest", "Oldest", "Game", "Title", "Recently Exported" };
    public IReadOnlyList<string> LibraryPeriodChoices { get; } = new[] { "Any time", "7 days", "30 days" };
    public string LibraryPeriod { get => _libraryPeriod; set { if (!LibraryPeriodChoices.Contains(value)) return; _libraryPeriod = value; OnPropertyChanged(); ApplyFilter(); } }
    public ObservableCollection<RecentClipExport> RecentExports { get; } = [];
    public bool FavoritesOnly { get => _favoritesOnly; set { if (_favoritesOnly == value) return; _favoritesOnly = value; OnPropertyChanged(); ApplyFilter(); } }
    public string GameFilter { get => _gameFilter; set { _gameFilter = value ?? "All Games"; OnPropertyChanged(); ApplyFilter(); } }
    public string LibrarySort { get => _librarySort; set { if (!LibrarySortChoices.Contains(value)) return; _librarySort = value; OnPropertyChanged(); ApplyFilter(); } }
    public string TagsDraft { get => _tagsDraft; set { _tagsDraft = value; OnPropertyChanged(); PublishOrganizationDirty(); } }
    public string GameDraft { get => _gameDraft; set { _gameDraft = value; OnPropertyChanged(); PublishOrganizationDirty(); } }
    public bool HasUnsavedOrganization => SelectedClip != null &&
        (_tagsDraft != SelectedClip.TagsText || _gameDraft != SelectedClip.GameName);
    public bool CanOrganizeClip => SelectedClip != null;
    public string EditorSaveState { get => _editorSaveState; private set { _editorSaveState = value; OnPropertyChanged(); } }
    public bool CanUndoTrim => _trimUndo.Count > 0 && CanEditClip;
    public bool CanLoadFilmstrip => CanEditClip && !IsCaptureActive && !IsLoadingFilmstrip && !_filmstripCleanupUnconfirmed;
    public IReadOnlyList<ClipMarker> ClipMarkers => SelectedClip?.Markers ?? (IReadOnlyList<ClipMarker>)Array.Empty<ClipMarker>();
    public IReadOnlyList<string> ExportAspectChoices => ExportAspectPresets.All;
    public string ExportAspectPreset
    {
        get => SelectedClip?.ExportAspectPreset ?? ExportAspectPresets.Original;
        set
        {
            if (SelectedClip is not { } clip || IsExporting || !ExportAspectPresets.IsValid(value) || clip.ExportAspectPreset == value) return;
            string previous = clip.ExportAspectPreset; bool ready = clip.IsExportReady;
            clip.ExportAspectPreset = value;
            try { clip.IsExportReady = false; _storage.AddOrUpdateClip(clip); OnSelectedClipChanged(); RefreshRecentExports(); EditorSaveState = "Export shape saved · original unchanged"; }
            catch (Exception error) { clip.ExportAspectPreset = previous; clip.IsExportReady = ready; EditorSaveState = "Couldn't save export shape: " + error.Message; }
            OnPropertyChanged(); OnPropertyChanged(nameof(ExportAspectDescription));
        }
    }
    public string ExportAspectDescription => ExportAspectPreset == ExportAspectPresets.Original
        ? "Preserves the original picture shape; may reduce resolution to fit the file limit."
        : ExportAspectPresets.Label(ExportAspectPreset) + " · Full picture fitted inside a black canvas; nothing is cropped. Resolution may reduce to fit the file limit.";

    private void PublishOrganizationDirty()
    {
        OnPropertyChanged(nameof(HasUnsavedOrganization));
        if (HasUnsavedOrganization) EditorSaveState = "Clip details not saved";
    }
    private void OrganizationSelectionChanged()
    {
        ClearFilmstrip(); _trimUndo.Clear();
        _tagsDraft = SelectedClip?.TagsText ?? ""; _gameDraft = SelectedClip?.GameName ?? "";
        foreach (string property in new[] { nameof(TagsDraft), nameof(GameDraft), nameof(HasUnsavedOrganization), nameof(ClipMarkers),
            nameof(CanUndoTrim), nameof(CanLoadFilmstrip), nameof(CanOrganizeClip), nameof(ExportAspectPreset), nameof(ExportAspectDescription) }) OnPropertyChanged(property);
        EditorSaveState = "Edits saved";
    }
    private void RefreshOrganizationFacets()
    {
        string selected = _gameFilter;
        var games = _allClips.Select(c => c.GameGroup).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(g => g, StringComparer.OrdinalIgnoreCase).ToArray();
        GameGroups.Clear(); GameGroups.Add("All Games"); foreach (string game in games) GameGroups.Add(game);
        _gameFilter = GameGroups.Contains(selected) ? selected : "All Games"; OnPropertyChanged(nameof(GameFilter));
        RefreshRecentExports();
    }
    private void RefreshRecentExports()
    {
        RecentExports.Clear(); _recentExportIds.Clear();
        foreach (var clip in _allClips.Where(c => c.LastExportedAtUtc != null && c.IsExportReady)
                     .OrderByDescending(c => c.LastExportedAtUtc).Take(40))
            if (ReadyShareFile.TryGet(clip, clip.LastExportTargetMb, out var path, _settings.ExportsFolder))
            {
                RecentExports.Add(new(clip.Id, clip.Title, path, clip.ExportedFileSizeBytes, clip.LastExportedAtUtc!.Value,
                    clip.ExportAspectPreset, clip.LastExportTargetMb));
                _recentExportIds.Add(clip.Id);
                if (RecentExports.Count == 20) break;
            }
    }
    public bool SelectRecentExport(string clipId)
    {
        var clip = _allClips.FirstOrDefault(c => c.Id == clipId);
        if (clip == null || !ReadyShareFile.TryGet(clip, clip.LastExportTargetMb, out _, _settings.ExportsFolder))
        { RefreshRecentExports(); return false; }
        SelectedClip = clip;
        if (!ReferenceEquals(SelectedClip, clip)) return false;
        TargetSizeMb = clip.LastExportTargetMb;
        return true;
    }
    public bool SaveOrganization()
    {
        if (SelectedClip is not { } clip) return false;
        var tags = clip.Tags; string game = clip.GameName;
        try
        {
            clip.Tags = ClipOrganization.ParseTags(TagsDraft); clip.GameName = ClipOrganization.NormalizeGame(GameDraft);
            _storage.AddOrUpdateClip(clip); clip.NotifyOrganizationChanged();
            _tagsDraft = clip.TagsText; _gameDraft = clip.GameName;
            OnPropertyChanged(nameof(TagsDraft)); OnPropertyChanged(nameof(GameDraft)); OnPropertyChanged(nameof(HasUnsavedOrganization));
            RefreshOrganizationFacets(); ApplyFilter(); EditorSaveState = "Clip details saved"; return true;
        }
        catch (Exception error) { clip.Tags = tags; clip.GameName = game; EditorSaveState = "Couldn't save details: " + error.Message; return false; }
    }
    public void ToggleFavorite()
    {
        if (SelectedClip is not { } clip) return;
        clip.IsFavorite = !clip.IsFavorite;
        try { _storage.AddOrUpdateClip(clip); clip.NotifyOrganizationChanged(); ApplyFilter(); EditorSaveState = "Favorite saved"; }
        catch (Exception error) { clip.IsFavorite = !clip.IsFavorite; EditorSaveState = "Couldn't save favorite: " + error.Message; }
    }
    public bool AddMarker(double positionSeconds)
    {
        if (SelectedClip is not { } clip || !CanEditClip) return false;
        var previous = clip.Markers;
        try
        {
            clip.Markers = ClipOrganization.ValidateMarkers([.. previous, new(Math.Round(positionSeconds, 3), "")], clip.DurationSeconds);
            _storage.AddOrUpdateClip(clip); clip.NotifyOrganizationChanged(); OnPropertyChanged(nameof(ClipMarkers)); EditorSaveState = "Marker saved"; return true;
        }
        catch (Exception error) { clip.Markers = previous; EditorSaveState = "Couldn't save marker: " + error.Message; return false; }
    }
    public void RemoveMarker(ClipMarker marker)
    {
        if (SelectedClip is not { } clip) return;
        if (!clip.Markers.Any(existing => ReferenceEquals(existing, marker))) return;
        var previous = clip.Markers; clip.Markers = previous.Where(existing => existing != marker).ToList();
        try { _storage.AddOrUpdateClip(clip); OnPropertyChanged(nameof(ClipMarkers)); EditorSaveState = "Marker removed"; }
        catch (Exception error) { clip.Markers = previous; EditorSaveState = "Couldn't remove marker: " + error.Message; }
    }
    public void ReportUnsavedTrim() => EditorSaveState = "Trim not saved · release the handle or press Enter to apply";
    public void ReportRevertedTrim() => EditorSaveState = HasUnsavedOrganization ? "Clip details not saved" : "Previous trim kept · no changes saved";
    private void RememberTrim(double start, double end)
    { if (_undoingTrim) return; if (_trimUndo.Count == 20) _trimUndo.RemoveAt(0); _trimUndo.Add((start, end)); OnPropertyChanged(nameof(CanUndoTrim)); }
    public bool UndoTrim()
    {
        if (!CanUndoTrim) return false;
        var previous = _trimUndo[^1]; _trimUndo.RemoveAt(_trimUndo.Count - 1);
        bool applied;
        _undoingTrim = true;
        try { applied = ApplyTrim(previous.Start, previous.End); }
        finally { _undoingTrim = false; }
        if (!applied) _trimUndo.Add(previous);
        OnPropertyChanged(nameof(CanUndoTrim)); return applied;
    }

    private CancellationTokenSource? _filmstripCancellation;
    private TaskCompletionSource? _filmstripCompletion;
    private int _filmstripGeneration;
    private bool _isLoadingFilmstrip;
    private bool _filmstripCleanupUnconfirmed;
    public ObservableCollection<TimelineFrame> FilmstripFrames { get; } = [];
    public bool IsLoadingFilmstrip { get => _isLoadingFilmstrip; private set { _isLoadingFilmstrip = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanLoadFilmstrip)); OnPropertyChanged(nameof(IsBackgroundWorkActive)); } }
    public long FilmstripDecodedBytes { get; private set; }
    public long FilmstripManagedDeltaBytes { get; private set; }
    public string FilmstripStatusText { get; private set; } = "Optional · Load six small preview frames when replay is off.";
    public void ClearFilmstrip()
    {
        _filmstripGeneration++; _filmstripCancellation?.Cancel(); FilmstripFrames.Clear(); FilmstripDecodedBytes = 0;
        FilmstripStatusText = _filmstripCleanupUnconfirmed ? "Preview worker exit was not confirmed. Quit and reopen Flick." : "Optional · Load six small preview frames when replay is off.";
        OnPropertyChanged(nameof(FilmstripStatusText));
        OnPropertyChanged(nameof(FilmstripDecodedBytes));
    }
    public async Task<bool> StopFilmstripAsync(bool allowUnconfirmedShutdown = false)
    {
        ClearFilmstrip();
        if (_filmstripCompletion is not { } completion) return allowUnconfirmedShutdown || !_filmstripCleanupUnconfirmed;
        try { await completion.Task.WaitAsync(TimeSpan.FromSeconds(6)); return allowUnconfirmedShutdown || !_filmstripCleanupUnconfirmed; }
        catch (TimeoutException) { return false; }
    }
    public async Task LoadFilmstripAsync()
    {
        if (SelectedClip is not { } clip || !clip.IsAvailable || !double.IsFinite(clip.DurationSeconds) || clip.DurationSeconds < .25 || !CanLoadFilmstrip) return;
        ClearFilmstrip(); int generation = _filmstripGeneration;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30)); _filmstripCancellation = cancellation;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); _filmstripCompletion = completion;
        IsLoadingFilmstrip = true;
        string cache = Path.Combine(string.IsNullOrWhiteSpace(_settings.TempFolder) ? Path.GetTempPath() : _settings.TempFolder,
            "FlickFilmstrip-" + Guid.NewGuid().ToString("N"));
        long before = GC.GetTotalMemory(false);
        try
        {
            Directory.CreateDirectory(cache);
            for (int frame = 0; frame < FilmstripFrameDecoder.FrameCount; frame++)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                double seconds = clip.DurationSeconds * (frame + .5) / FilmstripFrameDecoder.FrameCount;
                string file = Path.Combine(cache, frame + ".jpg");
                await _prober.GenerateThumbnailAsync(clip.OriginalFilePath, file, seconds, cancellation.Token);
                if (generation != _filmstripGeneration || SelectedClip?.Id != clip.Id || IsCaptureActive) return;
                if (new FileInfo(file).Length > 512 * 1024) throw new InvalidDataException("Timeline frame exceeded its file budget.");
                using var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
                var image = FilmstripFrameDecoder.Decode(input);
                FilmstripDecodedBytes += (long)image.PixelWidth * image.PixelHeight * 4;
                FilmstripFrames.Add(new(clip.Id, image, seconds, TrimTimelineMath.Format(seconds)));
            }
            FilmstripManagedDeltaBytes = Math.Max(0, GC.GetTotalMemory(false) - before);
            FilmstripStatusText = $"Six frames · decoded display pixels {FilmstripDecodedBytes / 1024} KiB (cap 338 KiB). Managed delta {FilmstripManagedDeltaBytes / 1024} KiB; includes concurrent app work.";
        }
        catch (OperationCanceledException) { if (generation == _filmstripGeneration) FilmstripStatusText = "Preview frames cancelled."; }
        catch (Exception error)
        {
            if (error is MediaWorkerExitUnconfirmedException)
            {
                _filmstripCleanupUnconfirmed = true;
                StatusDetails = "Preview worker exit was not confirmed. Quit and reopen Flick before recording or exporting.";
                OnPropertyChanged(nameof(CanControlReplay)); OnPropertyChanged(nameof(CanStartExport));
            }
            if (generation == _filmstripGeneration) FilmstripStatusText = "Frames unavailable · navigation still works. " + error.Message;
        }
        finally
        {
            if (ReferenceEquals(_filmstripCancellation, cancellation)) _filmstripCancellation = null;
            try { Directory.Delete(cache, true); } catch { }
            IsLoadingFilmstrip = false;
            if (ReferenceEquals(_filmstripCompletion, completion)) _filmstripCompletion = null;
            completion.TrySetResult();
            OnPropertyChanged(nameof(FilmstripDecodedBytes)); OnPropertyChanged(nameof(FilmstripManagedDeltaBytes)); OnPropertyChanged(nameof(FilmstripStatusText));
        }
    }
}
