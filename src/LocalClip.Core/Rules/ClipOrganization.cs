using LocalClip.Core.Models;

namespace LocalClip.Core.Rules;

public static class ClipOrganization
{
    public const int MaxTags = 12, MaxTagLength = 32, MaxMarkers = 32, MaxGameNameLength = 80;
    public static List<string> ParseTags(string? text) => NormalizeTags((text ?? "").Split([',', ';'], StringSplitOptions.RemoveEmptyEntries));
    public static List<string> NormalizeTags(IEnumerable<string> tags)
    {
        ArgumentNullException.ThrowIfNull(tags);
        var result = new List<string>();
        foreach (string? raw in tags)
        {
            if (raw == null) throw new ArgumentException("Tags cannot contain null entries.");
            string tag = raw.Trim().Normalize();
            if (tag.Length == 0) continue;
            if (tag.Length > MaxTagLength || tag.Any(char.IsControl) || tag.Contains(',') || tag.Contains(';'))
                throw new ArgumentException("Use tags of 1–32 characters without commas or control characters.");
            if (!result.Contains(tag, StringComparer.OrdinalIgnoreCase)) result.Add(tag);
            if (result.Count > MaxTags) throw new ArgumentException("Use at most 12 tags per clip.");
        }
        return result;
    }
    public static string NormalizeGame(string? value)
    {
        string name = (value ?? "").Trim().Normalize();
        if (name.Length > MaxGameNameLength || name.Any(char.IsControl))
            throw new ArgumentException("Use a game name of at most 80 characters.");
        return name;
    }
    public static List<ClipMarker> ValidateMarkers(IEnumerable<ClipMarker> markers, double duration)
    {
        ArgumentNullException.ThrowIfNull(markers);
        var result = new List<ClipMarker>();
        foreach (var marker in markers)
        {
            if (marker == null || !double.IsFinite(duration) || !double.IsFinite(marker.PositionSeconds) ||
                marker.PositionSeconds < 0 || marker.PositionSeconds > duration || marker.Label == null ||
                marker.Label.Length > 48 || marker.Label.Any(char.IsControl))
                throw new ArgumentException("Markers must have a valid time within the original clip and a short label.");
            if (result.Any(existing => Math.Abs(existing.PositionSeconds - marker.PositionSeconds) < .001))
                throw new ArgumentException("A marker already exists at this time.");
            string label = marker.Label.Trim();
            result.Add(label == marker.Label ? marker : marker with { Label = label });
            if (result.Count > MaxMarkers) throw new ArgumentException("Use at most 32 markers per clip.");
        }
        return result.OrderBy(marker => marker.PositionSeconds).ToList();
    }
}

/// <summary>In-memory search and sorting: no path probes, decoding or mutable global state.</summary>
public static class ClipLibraryQuery
{
    public static IReadOnlyList<ClipMetadata> Apply(IEnumerable<ClipMetadata> clips, string? search = null,
        string source = "All Clips", bool favoritesOnly = false, string game = "All Games", string sort = "Newest", DateTime? createdAfter = null)
    {
        string[] terms = (search ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var filtered = clips.Where(clip =>
            (source != "Flick" || !clip.IsImported) && (source != "Imported" || clip.IsImported) &&
            (createdAfter == null || clip.CreatedAt >= createdAfter) &&
            (!favoritesOnly || clip.IsFavorite) && (game == "All Games" || string.Equals(clip.GameGroup, game, StringComparison.OrdinalIgnoreCase)) &&
            terms.All(term => clip.Title.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                clip.GameGroup.Contains(term, StringComparison.OrdinalIgnoreCase) || clip.LibrarySource.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                clip.CreatedAt.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture).Contains(term, StringComparison.OrdinalIgnoreCase) ||
                clip.Tags.Any(tag => tag.Contains(term.TrimStart('#'), StringComparison.OrdinalIgnoreCase))));
        return sort switch
        {
            "Oldest" => filtered.OrderBy(c => c.CreatedAt).ThenBy(c => c.Id, StringComparer.Ordinal).ToList(),
            "Game" => filtered.OrderBy(c => c.GameGroup, StringComparer.OrdinalIgnoreCase).ThenByDescending(c => c.CreatedAt).ToList(),
            "Title" => filtered.OrderBy(c => c.Title, StringComparer.OrdinalIgnoreCase).ThenByDescending(c => c.CreatedAt).ToList(),
            "Recently Exported" => filtered.Where(c => c.LastExportedAtUtc != null).OrderByDescending(c => c.LastExportedAtUtc).ToList(),
            _ => filtered.OrderByDescending(c => c.CreatedAt).ThenBy(c => c.Id, StringComparer.Ordinal).ToList()
        };
    }
}
