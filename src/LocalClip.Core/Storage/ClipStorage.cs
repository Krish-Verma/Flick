using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using LocalClip.Core.Media;
using LocalClip.Core.Models;

namespace LocalClip.Core.Storage;

public class ClipStorage
{
    private readonly string _metadataFile;
    private readonly string _thumbnailsDir;
    private readonly MediaProber _prober;
    private readonly List<ClipMetadata> _clips = new();
    private readonly object _lock = new();

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT FileOp);

    private const int FO_DELETE = 0x0003;
    private const int FOF_ALLOWUNDO = 0x0040;
    private const int FOF_NOCONFIRMATION = 0x0010;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        [MarshalAs(UnmanagedType.U4)]
        public int wFunc;
        public string pFrom;
        public string pTo;
        public short fFlags;
        [MarshalAs(UnmanagedType.Bool)]
        public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string lpszProgressTitle;
    }

    public ClipStorage(AppSettings settings, MediaProber? prober = null)
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string metaDir = Path.Combine(localAppData, "LocalClip");
        Directory.CreateDirectory(metaDir);

        _metadataFile = Path.Combine(metaDir, "clips_index.json");
        _thumbnailsDir = Path.Combine(metaDir, "Thumbnails");
        Directory.CreateDirectory(_thumbnailsDir);

        _prober = prober ?? new MediaProber();
        LoadIndex();
    }

    public List<ClipMetadata> GetAllClips()
    {
        lock (_lock)
        {
            return _clips.OrderByDescending(c => c.CreatedAt).ToList();
        }
    }

    public ClipMetadata? GetClipById(string id)
    {
        lock (_lock)
        {
            return _clips.FirstOrDefault(c => c.Id == id);
        }
    }

    public void AddOrUpdateClip(ClipMetadata clip)
    {
        lock (_lock)
        {
            int idx = _clips.FindIndex(c => c.Id == clip.Id);
            if (idx >= 0)
            {
                _clips[idx] = clip;
            }
            else
            {
                _clips.Add(clip);
            }
            SaveIndex();
        }
    }

    public async Task<ClipMetadata> RegisterNewOriginalAsync(string originalPath, string targetName = "")
    {
        var probe = await _prober.ProbeFileAsync(originalPath);
        var fi = new FileInfo(originalPath);

        string title = Path.GetFileNameWithoutExtension(originalPath);
        string thumbPath = Path.Combine(_thumbnailsDir, $"{Path.GetFileNameWithoutExtension(originalPath)}_thumb.jpg");
        if (File.Exists(originalPath))
        {
            await _prober.GenerateThumbnailAsync(originalPath, thumbPath, Math.Min(1.0, probe.DurationSeconds / 2.0));
        }

        var meta = new ClipMetadata
        {
            OriginalFilePath = originalPath,
            Title = title,
            CreatedAt = fi.CreationTime,
            DurationSeconds = probe.DurationSeconds,
            FileSizeBytes = fi.Length,
            Width = probe.Width,
            Height = probe.Height,
            Fps = probe.Fps,
            TargetName = targetName,
            ThumbnailPath = thumbPath,
            AudioTracks = probe.AudioTracks,
            TrimStartSeconds = 0.0,
            TrimEndSeconds = probe.DurationSeconds
        };

        AddOrUpdateClip(meta);
        return meta;
    }

    public async Task RescanMediaFolderAsync(string originalsDir)
    {
        if (!Directory.Exists(originalsDir)) return;

        var files = Directory.GetFiles(originalsDir, "*.mkv", SearchOption.TopDirectoryOnly);
        foreach (var f in files)
        {
            bool exists;
            lock (_lock)
            {
                exists = _clips.Any(c => string.Equals(c.OriginalFilePath, f, StringComparison.OrdinalIgnoreCase));
            }
            if (!exists)
            {
                await RegisterNewOriginalAsync(f, "Discovered Original");
            }
        }
    }

    public bool RenameClip(string id, string newTitle)
    {
        lock (_lock)
        {
            var clip = _clips.FirstOrDefault(c => c.Id == id);
            if (clip == null) return false;

            clip.Title = newTitle;
            SaveIndex();
            return true;
        }
    }

    public bool DeleteClip(string id, bool moveToRecycleBin = true)
    {
        lock (_lock)
        {
            var clip = _clips.FirstOrDefault(c => c.Id == id);
            if (clip == null) return false;

            // Delete original file
            if (File.Exists(clip.OriginalFilePath))
            {
                if (moveToRecycleBin)
                {
                    SendToRecycleBin(clip.OriginalFilePath);
                }
                else
                {
                    File.Delete(clip.OriginalFilePath);
                }
            }

            // Delete export if exists
            if (!string.IsNullOrEmpty(clip.ExportedFilePath) && File.Exists(clip.ExportedFilePath))
            {
                try { File.Delete(clip.ExportedFilePath); } catch { }
            }

            // Delete thumbnail if exists
            if (!string.IsNullOrEmpty(clip.ThumbnailPath) && File.Exists(clip.ThumbnailPath))
            {
                try { File.Delete(clip.ThumbnailPath); } catch { }
            }

            _clips.Remove(clip);
            SaveIndex();
            return true;
        }
    }

    private static void SendToRecycleBin(string path)
    {
        try
        {
            var shf = new SHFILEOPSTRUCT
            {
                wFunc = FO_DELETE,
                pFrom = path + '\0' + '\0',
                fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION
            };
            SHFileOperation(ref shf);
        }
        catch
        {
            File.Delete(path);
        }
    }

    private void LoadIndex()
    {
        lock (_lock)
        {
            _clips.Clear();
            if (File.Exists(_metadataFile))
            {
                try
                {
                    string json = File.ReadAllText(_metadataFile);
                    var list = JsonSerializer.Deserialize<List<ClipMetadata>>(json);
                    if (list != null)
                    {
                        // Clean up entries whose originals no longer exist
                        foreach (var item in list)
                        {
                            if (File.Exists(item.OriginalFilePath))
                            {
                                _clips.Add(item);
                            }
                        }
                    }
                }
                catch { }
            }
        }
    }

    private void SaveIndex()
    {
        lock (_lock)
        {
            try
            {
                string temp = _metadataFile + ".tmp";
                string json = JsonSerializer.Serialize(_clips, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(temp, json);
                File.Move(temp, _metadataFile, overwrite: true);
            }
            catch { }
        }
    }
}
