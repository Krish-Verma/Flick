using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LocalClip.Core.Media;
using LocalClip.Core.Rules;
using LocalClip.Core.Models;

namespace LocalClip.Core.Storage;

/// <summary>
/// One local metadata index and one small folder registry. Linking a directory
/// never copies media, deletes source files or runs a permanent watcher.
/// </summary>
public sealed class ClipStorage
{
    private readonly string _metadataFile;
    private readonly string _sourcesFile;
    private readonly string _thumbnailsDir;
    private readonly MediaProber _prober;
    private readonly List<ClipMetadata> _clips = new();
    private readonly List<ConnectedClipFolder> _folders = new();
    private readonly HashSet<string> _ignoredImportedPaths = new(ClipLibraryPaths.FileComparer);
    private readonly object _lock = new();
    private readonly SemaphoreSlim _scanGate = new(1, 1);
    private int _unflushedImportedEntries;
    private bool _indexReadFailed;
    private bool _sourcesReadFailed;
    private const int ImportIndexBatchSize = 32;

    public string LastLoadWarning { get; private set; } = "";

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT fileOp);

    private const int FoDelete = 0x0003;
    private const int FofAllowUndo = 0x0040;
    private const int FofNoConfirmation = 0x0010;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        [MarshalAs(UnmanagedType.U4)] public int wFunc;
        public string pFrom;
        public string pTo;
        public short fFlags;
        [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string lpszProgressTitle;
    }

    private sealed class SourcesState
    {
        public int SchemaVersion { get; set; } = 1;
        public List<ConnectedClipFolder> Folders { get; set; } = new();
        public List<string> IgnoredImportedPaths { get; set; } = new();
    }

    /// <param name="metadataDirectory">Optional test-only isolated metadata root.</param>
    public ClipStorage(AppSettings settings, MediaProber? prober = null,
        string? metadataDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        string root = metadataDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LocalClip");
        Directory.CreateDirectory(root);
        _metadataFile = Path.Combine(root, "clips_index.json");
        _sourcesFile = Path.Combine(root, "connected_sources.json");
        _thumbnailsDir = Path.Combine(root, "Thumbnails");
        Directory.CreateDirectory(_thumbnailsDir);
        _prober = prober ?? new MediaProber();
        LoadIndex();
        LoadSources();
    }

    public List<ClipMetadata> GetAllClips()
    {
        lock (_lock)
            return _clips.OrderByDescending(c => c.CreatedAt).ToList();
    }

    public ClipMetadata? GetClipById(string id)
    {
        lock (_lock)
            return _clips.FirstOrDefault(c => c.Id == id);
    }

    public IReadOnlyList<ConnectedClipFolder> GetConnectedFolders()
    {
        lock (_lock)
            return _folders.Select(CopyFolder).ToList();
    }

    private static ConnectedClipFolder CopyFolder(ConnectedClipFolder f) => new()
    {
        Id = f.Id, Path = f.Path, DisplayName = f.DisplayName,
        IncludeSubfolders = f.IncludeSubfolders, ConnectedAtUtc = f.ConnectedAtUtc
    };

    public ConnectedClipFolder ConnectFolder(string path, bool includeSubfolders = true)
    {
        string normalized = ClipLibraryPaths.Normalize(path);
        ClipLibraryPaths.ValidateImportRoot(normalized);
        lock (_lock)
        {
            var existing = _folders.FirstOrDefault(f =>
                ClipLibraryPaths.FileComparer.Equals(f.Path, normalized));
            if (existing != null) return CopyFolder(existing);
            if (_folders.Count >= 32)
                throw new InvalidOperationException("Flick supports at most 32 linked folders. Unlink an unused folder or choose a parent directory.");

            var folder = new ConnectedClipFolder
            {
                Path = normalized,
                DisplayName = new DirectoryInfo(normalized).Name,
                IncludeSubfolders = includeSubfolders
            };
            _folders.Add(folder);
            try { SaveSourcesLocked(); }
            catch { _folders.Remove(folder); throw; }
            return CopyFolder(folder);
        }
    }

    public bool DisconnectFolder(string id)
    {
        lock (_lock)
        {
            var folder = _folders.FirstOrDefault(f => f.Id == id);
            if (folder is null) return false;
            var remainingClips = _clips.Where(c =>
                !(c.IsImported && c.ConnectedFolderId == id)).ToList();
            var ignored = _ignoredImportedPaths.Where(p =>
                !IsWithin(p, folder.Path)).ToList();
            // Do not delete any actual imported video or user-created export.
            _folders.Remove(folder);
            try { SaveSourcesLocked(); }
            catch { _folders.Add(folder); throw; }
            _clips.Clear();
            _clips.AddRange(remainingClips);
            _ignoredImportedPaths.Clear();
            foreach (var p in ignored) _ignoredImportedPaths.Add(p);
            SaveIndexLocked();
            SaveSourcesLocked();
            return true;
        }
    }

    public void AddOrUpdateClip(ClipMetadata clip)
    {
        ArgumentNullException.ThrowIfNull(clip);
        ValidateOrganization(clip);
        lock (_lock)
        {
            int idx = _clips.FindIndex(c => c.Id == clip.Id);
            ClipMetadata? previous = idx >= 0 ? _clips[idx] : null;
            if (idx >= 0) _clips[idx] = clip;
            else _clips.Add(clip);
            try { SaveIndexLocked(); }
            catch
            {
                if (previous != null) _clips[idx] = previous;
                else _clips.Remove(clip);
                throw;
            }
        }
    }

    private static void ValidateOrganization(ClipMetadata clip)
    {
        if (clip.GameName == null) throw new InvalidDataException("Clip index contains an invalid game label.");
        clip.Tags = ClipOrganization.NormalizeTags(clip.Tags);
        clip.GameName = ClipOrganization.NormalizeGame(clip.GameName);
        clip.Markers = ClipOrganization.ValidateMarkers(clip.Markers, clip.DurationSeconds);
        if (!ExportAspectPresets.IsValid(clip.ExportAspectPreset))
            throw new InvalidDataException("Clip index contains an invalid export aspect preset.");
        if (clip.LastExportTargetMb != 0 && !ShareExportTargets.IsValid(clip.LastExportTargetMb))
            throw new InvalidDataException("Clip index contains an invalid previous export size.");
    }

    public async Task<ClipMetadata> RegisterNewOriginalAsync(string originalPath,
        string targetName = "", IReadOnlyList<string>? trackNames = null,
        CancellationToken cancellationToken = default, bool generateThumbnail = true)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string normalized = ClipLibraryPaths.Normalize(originalPath);
        lock (_lock)
        {
            var duplicate = FindByPathLocked(normalized);
            if (duplicate != null) return duplicate;
        }
        return await RegisterOriginalAsync(normalized, targetName, null, trackNames,
            thumbnail: generateThumbnail, cancellationToken);
    }

    private async Task<ClipMetadata> RegisterOriginalAsync(
        string normalized, string targetName, ConnectedClipFolder? source,
        IReadOnlyList<string>? trackNames, bool thumbnail,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(normalized))
            throw new FileNotFoundException("Video file is unavailable.", normalized);

        var probe = await _prober.ProbeFileAsync(normalized, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (!probe.IsValid || probe.DurationSeconds <= 0 ||
            probe.Width <= 0 || probe.Height <= 0)
            throw new InvalidDataException("Video failed media validation: " +
                                           (probe.ErrorMessage ?? normalized));

        if (trackNames?.Count == probe.AudioTracks.Count)
            for (int i = 0; i < trackNames.Count; i++)
                if (!string.IsNullOrWhiteSpace(trackNames[i]))
                {
                    probe.AudioTracks[i].Name = trackNames[i];
                    if (source == null) probe.AudioTracks[i].Role = LocalClip.Core.Audio.AudioTrackRoles.FromNativeName(trackNames[i]);
                }

        if (source != null)
        {
            // An external OBS/SteelSeries MKV's first track is not necessarily
            // Flick's combined preview mix. Do not invent game/Discord labels
            // or automatically mix every unknown track twice.
            foreach (var track in probe.AudioTracks)
            {
                track.IsMuted = AudioMixDefaults.ShouldMute(
                    imported: true, probe.AudioTracks.Count, track.TrackIndex);
                if (track.Name == "Combined Audio")
                    track.Name = "Audio Track 1";
            }
        }

        if (source == null && trackNames?.Count == probe.AudioTracks.Count)
            LocalClip.Core.Audio.AudioTrackRoles.Reset(new ClipMetadata { AudioTracks = probe.AudioTracks });
        var info = new FileInfo(normalized);
        var clip = new ClipMetadata
        {
            OriginalFilePath = normalized,
            Title = Path.GetFileNameWithoutExtension(normalized),
            CreatedAt = info.CreationTime,
            DurationSeconds = probe.DurationSeconds,
            FileSizeBytes = info.Length,
            Width = probe.Width,
            Height = probe.Height,
            Fps = probe.Fps,
            TargetName = targetName,
            AudioTracks = probe.AudioTracks,
            TrimEndSeconds = probe.DurationSeconds,
            SourceKind = source == null ? "Flick" : "Imported",
            ConnectedFolderId = source?.Id ?? "",
            SourceLabel = source?.DisplayName ?? ""
        };

        // Only the first few imported clips get thumbnails during initial
        // indexing; large folders must not launch thousands of video decoders.
        if (thumbnail)
        {
            string thumb = Path.Combine(_thumbnailsDir, clip.Id + ".jpg");
            try
            {
                var generated = await _prober.GenerateThumbnailAsync(
                    normalized, thumb, Math.Min(1.0, probe.DurationSeconds / 2.0),
                    cancellationToken);
                clip.ThumbnailPath = generated;
            }
            catch (OperationCanceledException) { throw; }
            catch { /* Metadata and originals remain usable without a thumbnail. */ }
        }

        lock (_lock)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var existing = FindByPathLocked(normalized);
            if (existing != null) return existing;
            if (source != null && !_folders.Any(f => f.Id == source.Id))
                throw new InvalidOperationException("The folder was disconnected during indexing.");
            _clips.Add(clip);
            try
            {
                if (source == null)
                    SaveIndexLocked();
                else if (++_unflushedImportedEntries >= ImportIndexBatchSize)
                    SaveIndexLocked();
            }
            catch { _clips.Remove(clip); throw; }
        }
        return clip;
    }

    public async Task<ClipImportSummary> ScanConnectedFolderAsync(
        string folderId, IProgress<ClipImportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        await _scanGate.WaitAsync(cancellationToken);
        try
        {
            ConnectedClipFolder source;
            HashSet<string> existing;
            HashSet<string> ignored;
            lock (_lock)
            {
                source = CopyFolder(_folders.FirstOrDefault(f => f.Id == folderId)
                    ?? throw new ArgumentException("Unknown linked clip folder.", nameof(folderId)));
                existing = new HashSet<string>(
                    _clips.Select(c => SafeNormalize(c.OriginalFilePath)), ClipLibraryPaths.FileComparer);
                ignored = new HashSet<string>(_ignoredImportedPaths, ClipLibraryPaths.FileComparer);
            }

            if (!Directory.Exists(source.Path))
                return new ClipImportSummary { FolderUnavailable = true };

            // Explicit import only; enumeration is done on a worker thread.
            var media = await Task.Run(() => ClipLibraryPaths.DiscoverVideos(
                source.Path, source.IncludeSubfolders, cancellationToken), cancellationToken);
            int imported = 0, already = 0, skipped = 0, failed = 0, examined = 0;
            foreach (var path in media)
            {
                cancellationToken.ThrowIfCancellationRequested();
                examined++;
                if (ignored.Contains(path)) skipped++;
                else if (!existing.Add(path)) already++;
                else
                {
                    try
                    {
                        await RegisterOriginalAsync(path, source.DisplayName, source,
                            trackNames: null, thumbnail: imported < 16, cancellationToken);
                        imported++;
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception)
                    {
                        failed++;
                        existing.Remove(path); // Later manual rescan may retry.
                    }
                }
                if (examined % 25 == 0 || examined == media.Count)
                {
                    progress?.Report(new ClipImportProgress
                    {
                        Examined = examined, TotalDiscovered = media.Count,
                        Imported = imported, AlreadyIndexed = already,
                        Ignored = skipped, Failed = failed
                    });
                    if (examined % 100 == 0)
                        await Task.Delay(25, cancellationToken);
                }
            }
            return new ClipImportSummary
            {
                TotalDiscovered = media.Count, Imported = imported,
                AlreadyIndexed = already, Ignored = skipped, Failed = failed,
                ScanLimitReached = media.Count >= ClipLibraryPaths.MaximumMediaFilesPerScan
            };
        }
        finally
        {
            // Bound metadata writes while processing huge libraries and save
            // partial progress even when the user cancels to resume gameplay.
            try
            {
                lock (_lock)
                    if (_unflushedImportedEntries > 0) SaveIndexLocked();
            }
            finally { _scanGate.Release(); }
        }
    }

    public async Task RescanMediaFolderAsync(string originalsDir,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(originalsDir)) return;
        foreach (var file in Directory.EnumerateFiles(originalsDir, "*.mkv",
                     SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ClipLibraryPaths.IsReparsePoint(file)) continue;
            string path = ClipLibraryPaths.Normalize(file);
            bool alreadyKnown;
            lock (_lock) alreadyKnown = FindByPathLocked(path) != null;
            if (!alreadyKnown)
                await RegisterOriginalAsync(path, "Discovered Original", null,
                    trackNames: null, thumbnail: true, cancellationToken);
        }
    }

    public async Task<string> EnsureThumbnailAsync(string clipId,
        CancellationToken cancellationToken = default)
    {
        ClipMetadata? clip = GetClipById(clipId);
        if (clip is null || !clip.IsAvailable) return "";
        if (!string.IsNullOrWhiteSpace(clip.ThumbnailPath) &&
            File.Exists(clip.ThumbnailPath)) return clip.ThumbnailPath;
        string target = Path.Combine(_thumbnailsDir, clip.Id + ".jpg");
        string generated = await _prober.GenerateThumbnailAsync(
            clip.OriginalFilePath, target, Math.Min(1d, clip.DurationSeconds / 2d),
            cancellationToken);
        if (File.Exists(generated))
        {
            clip.ThumbnailPath = generated;
            AddOrUpdateClip(clip);
        }
        return generated;
    }

    public bool RenameClip(string id, string newTitle)
    {
        if (string.IsNullOrWhiteSpace(newTitle)) return false;
        lock (_lock)
        {
            var clip = _clips.FirstOrDefault(c => c.Id == id);
            if (clip is null) return false;
            string original = clip.Title;
            clip.Title = newTitle.Trim();
            try { SaveIndexLocked(); return true; }
            catch { clip.Title = original; throw; }
        }
    }

    /// <summary>Never deletes an externally linked source file.</summary>
    public bool DeleteClip(string id, bool moveToRecycleBin = true)
    {
        lock (_lock)
        {
            var clip = _clips.FirstOrDefault(c => c.Id == id);
            if (clip is null) return false;
            if (clip.IsImported)
            {
                string key = SafeNormalize(clip.OriginalFilePath);
                _ignoredImportedPaths.Add(key);
                SaveSourcesLocked();
                _clips.Remove(clip);
                SaveIndexLocked();
                return true;
            }

            if (File.Exists(clip.OriginalFilePath))
            {
                if (moveToRecycleBin)
                {
                    if (!SendToRecycleBin(clip.OriginalFilePath)) return false;
                }
                else File.Delete(clip.OriginalFilePath);
            }

            // Existing Flick-original deletion semantics, never applied to imports.
            if (!string.IsNullOrEmpty(clip.ExportedFilePath) &&
                File.Exists(clip.ExportedFilePath))
            {
                try { File.Delete(clip.ExportedFilePath); } catch { }
            }
            _clips.Remove(clip);
            SaveIndexLocked();
            return true;
        }
    }

    private static bool SendToRecycleBin(string path)
    {
        try
        {
            var shf = new SHFILEOPSTRUCT
            {
                wFunc = FoDelete,
                pFrom = path + '\0' + '\0',
                fFlags = FofAllowUndo | FofNoConfirmation
            };
            return SHFileOperation(ref shf) == 0 &&
                   !shf.fAnyOperationsAborted && !File.Exists(path);
        }
        catch { return false; }
    }

    private ClipMetadata? FindByPathLocked(string path) =>
        _clips.FirstOrDefault(c => ClipLibraryPaths.FileComparer.Equals(
            SafeNormalize(c.OriginalFilePath), path));

    private static string SafeNormalize(string path)
    {
        try { return ClipLibraryPaths.Normalize(path); }
        catch { return path; }
    }

    private static bool IsWithin(string path, string root) =>
        ClipLibraryPaths.FileComparer.Equals(path, root) ||
        path.StartsWith(root + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);

    private void LoadIndex()
    {
        lock (_lock)
        {
            if (!File.Exists(_metadataFile)) return;
            try
            {
                var data = JsonSerializer.Deserialize<List<ClipMetadata>>(
                    File.ReadAllText(_metadataFile)) ?? throw new InvalidDataException("Clip index contains no library array.");
                var seen = new HashSet<string>(ClipLibraryPaths.FileComparer);
                var validated = new List<ClipMetadata>();
                foreach (var clip in data)
                {
                    // JSON nulls bypass non-null property initializers. Reject
                    // damaged records before the library exposes them to search,
                    // audio editing or source-ownership-sensitive deletion.
                    if (clip is null || string.IsNullOrWhiteSpace(clip.Id) ||
                        clip.Title is null || clip.TargetName is null ||
                        clip.AudioTracks is null || clip.AudioTracks.Any(track => track is null) ||
                        (!string.Equals(clip.SourceKind, "Flick", StringComparison.OrdinalIgnoreCase) &&
                         !string.Equals(clip.SourceKind, "Imported", StringComparison.OrdinalIgnoreCase)))
                        throw new InvalidDataException("Clip index contains a damaged entry. Back up or repair the original index before editing the library.");
                    if (string.IsNullOrWhiteSpace(clip.OriginalFilePath)) continue;
                    ValidateOrganization(clip); // Missing legacy fields keep safe model defaults; invalid fields preserve raw index.
                    var path = SafeNormalize(clip.OriginalFilePath);
                    if (seen.Add(path))
                    {
                        clip.OriginalFilePath = path;
                        validated.Add(clip); // Keep OFFLINE entries across unplug/restart.
                    }
                }
                // A later bad record cannot leave a partially loaded index that
                // appears complete. Existing recovery guards preserve raw bytes.
                _clips.AddRange(validated);
            }
            catch (Exception ex)
            {
                _indexReadFailed = true;
                LastLoadWarning = "Clip index unavailable; original metadata was preserved. " + ex.Message;
                // Leave the original JSON untouched for recovery.
            }
        }
    }

    private void LoadSources()
    {
        lock (_lock)
        {
            if (!File.Exists(_sourcesFile)) return;
            try
            {
                var data = JsonSerializer.Deserialize<SourcesState>(
                    File.ReadAllText(_sourcesFile));
                if (data == null) return;
                var seen = new HashSet<string>(ClipLibraryPaths.FileComparer);
                foreach (var folder in data.Folders)
                {
                    string path = SafeNormalize(folder.Path);
                    if (string.IsNullOrWhiteSpace(path) || !seen.Add(path)) continue;
                    folder.Path = path;
                    _folders.Add(folder); // Offline folder registration persists.
                }
                foreach (var path in data.IgnoredImportedPaths)
                {
                    if (!string.IsNullOrWhiteSpace(path))
                        _ignoredImportedPaths.Add(SafeNormalize(path));
                }
            }
            catch (Exception ex)
            {
                _sourcesReadFailed = true;
                LastLoadWarning = "Connected folder registry unavailable; original metadata was preserved. " + ex.Message;
            }
        }
    }

    private void SaveIndexLocked()
    {
        if (_indexReadFailed)
            throw new InvalidDataException("Flick preserved a damaged clips_index.json; back up or repair it before editing the library.");
        SaveJsonAtomic(_metadataFile, _clips);
        _unflushedImportedEntries = 0;
    }

    private void SaveSourcesLocked()
    {
        if (_sourcesReadFailed)
            throw new InvalidDataException("Flick preserved a damaged connected_sources.json; back up or repair it before linking folders.");
        SaveJsonAtomic(_sourcesFile, new SourcesState
        {
            Folders = _folders,
            IgnoredImportedPaths = _ignoredImportedPaths.ToList()
        });
    }

    private static void SaveJsonAtomic<T>(string path, T value)
    {
        string temp = path + ".tmp";
        string json = JsonSerializer.Serialize(value, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        File.WriteAllText(temp, json);
        File.Move(temp, path, overwrite: true);
    }
}
