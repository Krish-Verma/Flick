using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace LocalClip.Core.Storage;

/// <summary>
/// Local library path and enumeration rules. No traversal into junctions or
/// symbolic links; no unbounded walk of arbitrary drives.
/// </summary>
public static class ClipLibraryPaths
{
    public const int MaximumMediaFilesPerScan = 10000;
    public const int MaximumDirectoriesPerScan = 1500;
    public const int MaximumDirectoryDepth = 8;

    public static readonly StringComparer FileComparer = StringComparer.OrdinalIgnoreCase;

    public static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            throw new ArgumentException("Choose a fully qualified path.", nameof(path));

        var canonical = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path.Trim()));
        if (canonical.Length == 0) throw new ArgumentException("The path is empty.", nameof(path));
        return canonical;
    }

    public static bool IsSupportedVideo(string path)
    {
        string ext = Path.GetExtension(path);
        return ext.Equals(".mkv", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".mp4", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsReparsePoint(string path)
    {
        try { return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; }
        catch (IOException) { return true; }
        catch (UnauthorizedAccessException) { return true; }
    }

    public static void ValidateImportRoot(string folder)
    {
        var path = Normalize(folder);
        if (!Directory.Exists(path))
            throw new DirectoryNotFoundException("The selected clip folder does not exist: " + path);
        if (IsReparsePoint(path))
            throw new IOException("Link the original folder instead of a symlink or junction.");
        if (FileComparer.Equals(path, Path.GetPathRoot(path)))
            throw new ArgumentException("Select a clips folder, not an entire drive.", nameof(folder));
    }

    public static IReadOnlyList<string> DiscoverVideos(
        string folder,
        bool includeSubfolders,
        CancellationToken cancellationToken = default)
    {
        ValidateImportRoot(folder);
        var results = new List<string>();
        var visited = new HashSet<string>(FileComparer);
        var pending = new Queue<(string Directory, int Depth)>();
        pending.Enqueue((Normalize(folder), 0));

        while (pending.Count > 0 && results.Count < MaximumMediaFilesPerScan)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (directory, depth) = pending.Dequeue();
            if (!visited.Add(directory)) continue;
            if (visited.Count > MaximumDirectoriesPerScan)
                break;

            try
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (IsReparsePoint(entry)) continue;
                    FileAttributes attributes;
                    try { attributes = File.GetAttributes(entry); }
                    catch (IOException) { continue; }
                    catch (UnauthorizedAccessException) { continue; }

                    if ((attributes & FileAttributes.Directory) != 0)
                    {
                        if (includeSubfolders && depth < MaximumDirectoryDepth &&
                            visited.Count + pending.Count < MaximumDirectoriesPerScan)
                        {
                            pending.Enqueue((Normalize(entry), depth + 1));
                        }
                    }
                    else if (IsSupportedVideo(entry))
                    {
                        results.Add(Normalize(entry));
                        if (results.Count >= MaximumMediaFilesPerScan) break;
                    }
                }
            }
            catch (IOException) { /* Unavailable folder: don't traverse elsewhere. */ }
            catch (UnauthorizedAccessException) { /* Don't escalate access. */ }
        }

        return results;
    }
}
