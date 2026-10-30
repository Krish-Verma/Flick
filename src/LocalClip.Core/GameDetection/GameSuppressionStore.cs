using System.Text.Json;

namespace LocalClip.Core.GameDetection;

/// <summary>Explicit user pauses only; attempts and temporary window loss are never persisted.</summary>
public sealed class GameSuppressionStore(string path)
{
    public const int MaximumEntries = 128;
    private sealed record State(int SchemaVersion, List<GameProcessIdentity> Suppressed);
    public IReadOnlyList<GameProcessIdentity> Load()
    {
        RefuseLinks(path);
        if (!File.Exists(path)) return [];
        if (new FileInfo(path).Length > 512 * 1024) throw new InvalidDataException("Game replay state is too large; automatic recording is paused.");
        var state = JsonSerializer.Deserialize<State>(File.ReadAllBytes(path));
        if (state == null || state.SchemaVersion is not (1 or 2) || state.Suppressed == null || state.Suppressed.Count > MaximumEntries ||
            state.Suppressed.Any(identity => identity == null || !identity.IsComplete) ||
            state.Suppressed.Select(identity => identity.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count() != state.Suppressed.Count)
            throw new InvalidDataException("Game replay state is unreadable; automatic recording is paused.");
        // Version 1 mixed successful starts, failed attempts and manual pauses.
        // Carrying those reservations forward made a running game stay disabled
        // after every application restart. Version 2 stores actual Stop intent.
        // Validate old bytes first, but leave them untouched until the next Save.
        return state.SchemaVersion == 1 ? [] : state.Suppressed.AsReadOnly();
    }

    public void Save(IReadOnlyList<GameProcessIdentity> identities)
    {
        if (identities.Count > MaximumEntries || identities.Any(identity => !identity.IsComplete))
            throw new InvalidDataException("Too many paused game sessions; automatic recording is paused.");
        string directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        RefuseLinks(directory);
        Directory.CreateDirectory(directory);
        RefuseLinks(path);
        string temporary = Path.Combine(directory, ".game-replay-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(file, new State(2, identities.ToList()));
                file.Flush(true);
            }
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static void RefuseLinks(string path)
    {
        string full = Path.GetFullPath(path), current = Path.GetPathRoot(full)!;
        foreach (string part in full[current.Length..].Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (part.Length == 0) continue;
            current = Path.Combine(current, part);
            if (Path.Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Automatic game replay state cannot use a redirected path.");
        }
    }
}
